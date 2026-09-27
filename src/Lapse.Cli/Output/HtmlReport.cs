using System.Globalization;
using System.Text;
using System.Text.Json;
using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Core.Items;

namespace Lapse.Cli.Output;

internal sealed record ReportRow(string Status, string Kind, string Name, string Owner, DateTimeOffset? ExpiresAt, string? Note);

internal static class HtmlReport
{
    private const string TemplateResource = "Lapse.report.html";
    private const string DataPlaceholder = "__LAPSE_DATA__";

    public static IEnumerable<ReportRow> FromItems(IEnumerable<ItemRow> rows) =>
        rows.Select(row => new ReportRow(
            row.Health.ToString().ToLowerInvariant(),
            row.Item.Key.Kind.Label(),
            row.Item.Name,
            row.Item.Owner,
            row.Item.ExpiresAt,
            row.Note));

    public static Result<IReadOnlyList<ReportRow>> FromExport(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var rows = document.RootElement.EnumerateArray()
                .Select(item => new ReportRow(
                    item.GetProperty("status").GetString()!,
                    item.GetProperty("kind").GetString()!,
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("owner").GetString()!,
                    item.GetProperty("expiresAt").ValueKind == JsonValueKind.String ? item.GetProperty("expiresAt").GetDateTimeOffset() : null,
                    item.TryGetProperty("note", out var note) ? note.GetString() : null))
                .ToList();
            return Result.Success<IReadOnlyList<ReportRow>>(rows);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Result.Failure<IReadOnlyList<ReportRow>>("the file was not created by lapse export");
        }
    }

    public static void Write(Stream destination, IEnumerable<ReportRow> rows, DateTimeOffset now)
    {
        using var template = new StreamReader(typeof(HtmlReport).Assembly.GetManifestResourceStream(TemplateResource)!);
        var html = template.ReadToEnd().Replace(DataPlaceholder, Data(rows, now), StringComparison.Ordinal);
        destination.Write(Encoding.UTF8.GetBytes(html));
    }

    private static string Data(IEnumerable<ReportRow> rows, DateTimeOffset now)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("generatedAt", AlertMessage.FormatDate(now));
            writer.WriteStartArray("items");
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                writer.WriteString("status", row.Status);
                writer.WriteString("kind", row.Kind);
                writer.WriteString("name", row.Name);
                writer.WriteString("owner", row.Owner);
                if (row.ExpiresAt is { } expiresAt)
                {
                    writer.WriteString("expires", ItemRows.FormatDate(expiresAt));
                    writer.WriteNumber("days", AlertPolicy.DaysRemaining(expiresAt, now));
                }

                writer.WriteString("note", row.Note);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
