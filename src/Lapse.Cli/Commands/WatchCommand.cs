using System.CommandLine;
using System.Globalization;
using Lapse.Cli.Output;

namespace Lapse.Cli.Commands;

internal static class WatchCommand
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(5);

    public static Command Create()
    {
        var every = new Option<string>("--every")
        {
            Description = "Time between scans: 30m, 6h or 1d (at least 5m).",
            DefaultValueFactory = _ => "6h",
        };
        every.Validators.Add(result =>
        {
            if (TryParseInterval(result.GetValueOrDefault<string>()) is null)
            {
                result.AddError("--every must be a number followed by m, h or d, and at least 5m. Example: 6h");
            }
        });

        var command = new Command("watch", "Scan periodically. Meant for Docker or a system service.") { every };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var text = parse.GetValue(every)!;
            var interval = TryParseInterval(text)!.Value;
            Terminal.Line($"Watching every {text}. Press Ctrl+C to stop.");

            for (var first = true; !cancellationToken.IsCancellationRequested; first = false)
            {
                var exitCode = await LapseRuntime.RunAsync(parse, runtime =>
                    ScanCommand.RunAsync(runtime, new ScanOptions(ShowAll: false, Notify: true, Json: false), cancellationToken), cancellationToken);

                if (first && exitCode == ExitCodes.ConfigurationError)
                {
                    return exitCode;
                }

                try
                {
                    await Task.Delay(interval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return ExitCodes.Success;
        });
        return command;
    }

    private static TimeSpan? TryParseInterval(string? text)
    {
        if (text is not { Length: >= 2 } || !int.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        TimeSpan? interval = text[^1] switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => null,
        };

        return interval >= MinimumInterval ? interval : null;
    }
}
