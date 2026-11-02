using System.CommandLine;
using System.Text.Json;
using Lapse.Cli.Output;
using Lapse.Core.Alerts;
using Lapse.Core.Scanning;

namespace Lapse.Cli.Commands;

internal sealed record ScanOptions(bool ShowAll, bool Notify, bool Json);

internal static class ScanCommand
{
    public static Command Create()
    {
        var all = new Option<bool>("--all") { Description = "Also show unchanged items." };
        var noNotify = new Option<bool>("--no-notify") { Description = "Scan without sending alerts." };
        var json = GlobalOptions.Json();

        var command = new Command("scan", "Check everything configured, store the result and send any due alerts.") { all, noNotify, json };
        command.SetAction((parse, cancellationToken) => LapseRuntime.RunAsync(parse, runtime =>
            RunAsync(runtime, new ScanOptions(parse.GetValue(all), !parse.GetValue(noNotify), parse.GetValue(json)), cancellationToken), cancellationToken));
        return command;
    }

    public static async Task<int> RunAsync(LapseRuntime runtime, ScanOptions options, CancellationToken cancellationToken)
    {
        if (!options.Json)
        {
            Terminal.Line($"Scanning {Terminal.Count(runtime.Plan.Targets.Count, "item", "items")}…");
            Terminal.Line();
        }

        var report = await runtime.Pipeline.RunAsync(runtime.Plan, options.Notify, cancellationToken);
        var rows = report.Entries
            .Select(entry => (entry.Outcome, Row: new ItemRow(entry.Item, runtime.Plan.Policy.HealthOf(entry.Item, report.ScannedAt), NoteFor(entry))))
            .ToList();

        if (options.Json)
        {
            ItemRows.WriteDocumentToOutput(writer => WriteJson(writer, report, rows.Select(pair => pair.Row)));
        }
        else
        {
            var visible = rows
                .Where(pair => options.ShowAll || pair.Row.Health != ItemHealth.Ok || pair.Outcome != ScanOutcome.Unchanged)
                .Select(pair => pair.Row)
                .ToList();

            ItemRows.WriteTable(visible, report.ScannedAt);
            WriteSummary(runtime, options, report, hidden: rows.Count - visible.Count);
        }

        return report.Delivery.Failures.Count > 0 ? ExitCodes.DeliveryFailed : ExitCodes.Success;
    }

    private static string? NoteFor(Reconciliation entry)
    {
        var note = entry.Outcome switch
        {
            ScanOutcome.Failed => entry.Item.LastError,
            ScanOutcome.Renewed => $"was: {ItemRows.FormatDate(entry.PreviousExpiresAt)}",
            ScanOutcome.Changed => $"expiry moved earlier; was: {ItemRows.FormatDate(entry.PreviousExpiresAt)}",
            ScanOutcome.New => "new",
            _ => null,
        };

        return entry.Outcome == ScanOutcome.Failed || entry.Item.Problem is null
            ? note
            : string.Join("; ", new[] { note, entry.Item.Problem }.OfType<string>());
    }

    private static void WriteSummary(LapseRuntime runtime, ScanOptions options, ScanReport report, int hidden)
    {
        Terminal.Line();
        if (hidden > 0)
        {
            Terminal.Line(Terminal.Paint($"{Terminal.Count(hidden, "unchanged item is", "unchanged items are")} hidden (use --all)", Tone.Muted));
        }

        if (!options.Notify)
        {
            Terminal.Line("Alerts disabled (--no-notify).");
            return;
        }

        if (!runtime.Pipeline.HasNotifiers)
        {
            Terminal.Line("No alert channels configured (notify.email, webhook, teams or telegram).");
            return;
        }

        var delivery = report.Delivery;
        Terminal.Line($"Alerts sent: {delivery.Sent} · already sent before (skipped): {delivery.Skipped}");
        foreach (var failure in delivery.Failures)
        {
            var what = failure.Keys.Count == 1 ? $"the alert for {failure.Keys[0].Value}" : Terminal.Count(failure.Keys.Count, "alert", "alerts");
            Terminal.Error($"Could not send {what} via {failure.Channel}: {failure.Error}");
        }

        if (delivery.Failures.Count > 0)
        {
            Terminal.Line("They will be retried on the next scan. To check a channel: lapse notify test");
        }

        if (delivery.Unreachable.Count > 0)
        {
            Terminal.Error($"No alert channel reaches the owner of {string.Join(", ", delivery.Unreachable.Select(key => key.Value))}. Give the owner an email address or add a chat channel.");
        }
    }

    private static void WriteJson(Utf8JsonWriter writer, ScanReport report, IEnumerable<ItemRow> rows)
    {
        writer.WriteStartObject();
        writer.WriteString("scannedAt", report.ScannedAt);
        writer.WritePropertyName("items");
        ItemRows.WriteJson(writer, rows, report.ScannedAt);
        writer.WriteStartObject("alerts");
        writer.WriteNumber("sent", report.Delivery.Sent);
        writer.WriteNumber("skipped", report.Delivery.Skipped);
        writer.WriteStartArray("failures");
        foreach (var failure in report.Delivery.Failures)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("keys");
            foreach (var key in failure.Keys)
            {
                writer.WriteStringValue(key.Value);
            }

            writer.WriteEndArray();
            writer.WriteString("channel", failure.Channel);
            writer.WriteString("error", failure.Error);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("unreachable");
        foreach (var key in report.Delivery.Unreachable)
        {
            writer.WriteStringValue(key.Value);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
