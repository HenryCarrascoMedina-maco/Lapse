namespace Lapse.Cli.Output;

internal sealed record Cell(string Text, Tone Tone = Tone.Plain);

internal static class ConsoleTable
{
    public static void Write(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<Cell>> rows)
    {
        var widths = headers
            .Select((header, column) => rows.Select(row => row[column].Text.Length).Append(header.Length).Max())
            .ToArray();

        Terminal.Line(Render(headers.Select(header => new Cell(header, Tone.Muted)).ToList(), widths));
        foreach (var row in rows)
        {
            Terminal.Line(Render(row, widths));
        }
    }

    private static string Render(IReadOnlyList<Cell> cells, int[] widths)
    {
        var last = cells.Count - 1;
        var rendered = cells.Select((cell, column) =>
            Terminal.Paint(column == last ? cell.Text : cell.Text.PadRight(widths[column]), cell.Tone));
        return " " + string.Join("  ", rendered);
    }
}
