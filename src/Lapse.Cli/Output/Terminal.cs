namespace Lapse.Cli.Output;

internal enum Tone
{
    Plain,
    Critical,
    Warning,
    Good,
    Muted,
}

internal static class Terminal
{
    private static readonly bool ColorsDisabled = Environment.GetEnvironmentVariable("NO_COLOR") is not null;

    public static void Line(string text = "") => Console.Out.WriteLine(text);

    public static string Count(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";

    public static void Error(string text) => Console.Error.WriteLine(Paint(text, Tone.Critical, Console.IsErrorRedirected));

    public static string Paint(string text, Tone tone) => Paint(text, tone, Console.IsOutputRedirected);

    private static string Paint(string text, Tone tone, bool redirected) =>
        tone == Tone.Plain || redirected || ColorsDisabled
            ? text
            : $"\u001b[{AnsiCode(tone)}m{text}\u001b[0m";

    private static string AnsiCode(Tone tone) => tone switch
    {
        Tone.Critical => "31",
        Tone.Warning => "33",
        Tone.Good => "32",
        _ => "90",
    };
}
