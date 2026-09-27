using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Infrastructure.Serialization;

namespace Lapse.Cli.Output;

internal sealed record ItemRow(Item Item, ItemHealth Health, string? Note = null);

internal static class ItemRows
{
    private static readonly string[] Headers = ["STATUS", "ITEM", "KIND", "EXPIRES", "DAYS", "OWNER", ""];

    public static IEnumerable<ItemRow> ForItems(IEnumerable<Item> items, AlertPolicy policy, DateTimeOffset now) =>
        items.Select(item => new ItemRow(item, policy.HealthOf(item, now), item.LastError));

    public static void WriteTable(IEnumerable<ItemRow> rows, DateTimeOffset now)
    {
        var cells = rows
            .OrderBy(row => row.Item.ExpiresAt ?? DateTimeOffset.MaxValue)
            .Select(row => Cells(row, now))
            .ToList();
        ConsoleTable.Write(Headers, cells);
    }

    public static void WriteJson(Utf8JsonWriter writer, IEnumerable<ItemRow> rows, DateTimeOffset now)
    {
        writer.WriteStartArray();
        foreach (var row in rows)
        {
            writer.WriteStartObject();
            ItemJson.WriteProperties(writer, row.Item, now);
            writer.WriteString("status", row.Health.ToString().ToLowerInvariant());
            writer.WriteString("note", row.Note);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static void WriteDocument(Stream destination, Action<Utf8JsonWriter> write)
    {
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var writer = new Utf8JsonWriter(destination, options);
        write(writer);
        writer.Flush();
        destination.Write("\n"u8);
    }

    public static void WriteDocumentToOutput(Action<Utf8JsonWriter> write)
    {
        using var output = Console.OpenStandardOutput();
        WriteDocument(output, write);
    }

    public static string FormatDate(DateTimeOffset? date) =>
        date?.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    private static List<Cell> Cells(ItemRow row, DateTimeOffset now)
    {
        var (label, tone) = Describe(row.Health);
        var days = row.Item.ExpiresAt is { } expiresAt
            ? AlertPolicy.DaysRemaining(expiresAt, now).ToString(CultureInfo.InvariantCulture)
            : "—";

        return
        [
            new Cell(label, tone),
            new Cell(row.Item.Name),
            new Cell(row.Item.Key.Kind.Label()),
            new Cell(FormatDate(row.Item.ExpiresAt)),
            new Cell(days.PadLeft(4)),
            new Cell(row.Item.Owner),
            new Cell(row.Note ?? string.Empty, Tone.Muted),
        ];
    }

    private static (string Label, Tone Tone) Describe(ItemHealth health) => health switch
    {
        ItemHealth.Expired => ("EXPIRED", Tone.Critical),
        ItemHealth.Critical => ("CRITICAL", Tone.Critical),
        ItemHealth.Warning => ("WARNING", Tone.Warning),
        ItemHealth.Ok => ("OK", Tone.Good),
        ItemHealth.Renewed => ("RENEWED", Tone.Good),
        ItemHealth.Error => ("ERROR", Tone.Warning),
        _ => ("—", Tone.Muted),
    };
}
