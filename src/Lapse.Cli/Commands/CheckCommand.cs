using System.CommandLine;
using Lapse.Cli.Output;
using Lapse.Core.Alerts;

namespace Lapse.Cli.Commands;

internal static class CheckCommand
{
    public static Command Create()
    {
        var minDays = new Option<int>("--min-days")
        {
            Description = "Fail if anything expires in fewer than this many days.",
            DefaultValueFactory = _ => 14,
        };
        minDays.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() < 0)
            {
                result.AddError("--min-days cannot be negative.");
            }
        });

        var ignoreErrors = new Option<bool>("--ignore-errors") { Description = "Do not fail because of items that could not be verified." };
        var json = GlobalOptions.Json();

        var command = new Command("check", "Scan without sending alerts and exit with an error if anything expires soon. Meant for CI.")
        {
            minDays,
            ignoreErrors,
            json,
        };

        command.SetAction((parse, cancellationToken) => LapseRuntime.RunAsync(parse, runtime =>
            RunAsync(runtime, parse.GetValue(minDays), parse.GetValue(ignoreErrors), parse.GetValue(json), cancellationToken), cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(LapseRuntime runtime, int minDays, bool ignoreErrors, bool json, CancellationToken cancellationToken)
    {
        var report = await runtime.Pipeline.RunAsync(runtime.Plan, notify: false, cancellationToken);
        var now = report.ScannedAt;
        var rows = ItemRows.ForItems(report.Entries.Select(entry => entry.Item), runtime.Plan.Policy, now).ToList();

        var expiring = rows.Where(row => row.Item.ExpiresAt is { } expiresAt && AlertPolicy.DaysRemaining(expiresAt, now) < minDays).ToList();
        var unverified = ignoreErrors ? [] : rows.Where(row => row.Health == ItemHealth.Error).ToList();
        var problems = expiring.Union(unverified).ToList();

        if (json)
        {
            ItemRows.WriteDocumentToOutput(writer =>
            {
                writer.WriteStartObject();
                writer.WriteBoolean("passed", problems.Count == 0);
                writer.WriteNumber("minDays", minDays);
                writer.WritePropertyName("problems");
                ItemRows.WriteJson(writer, problems, now);
                writer.WriteEndObject();
            });
        }
        else if (problems.Count == 0)
        {
            Terminal.Line(Terminal.Paint($"All good: nothing expires in the next {minDays} days.", Tone.Good));
        }
        else
        {
            ItemRows.WriteTable(problems, now);
            Terminal.Line();
            if (expiring.Count > 0)
            {
                Terminal.Error($"{Terminal.Count(expiring.Count, "item expires", "items expire")} in fewer than {minDays} days.");
            }

            if (unverified.Count > 0)
            {
                Terminal.Error($"{Terminal.Count(unverified.Count, "item", "items")} could not be verified.");
            }
        }

        return problems.Count == 0 ? ExitCodes.Success : ExitCodes.CheckFailed;
    }
}
