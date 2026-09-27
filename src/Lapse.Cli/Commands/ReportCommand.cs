using System.CommandLine;
using Lapse.Cli.Output;

namespace Lapse.Cli.Commands;

internal static class ReportCommand
{
    public static Command Create()
    {
        var output = new Option<string>("--output", "-o")
        {
            Description = "Destination HTML file.",
            DefaultValueFactory = _ => "lapse-report.html",
        };
        var from = new Option<string?>("--from")
        {
            Description = "Build the report from a file created by lapse export instead of the local database.",
        };

        var command = new Command("report", "Create a visual HTML report of the inventory that opens in any browser, even offline.") { output, from };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var destination = Path.GetFullPath(parse.GetRequiredValue(output));

            if (parse.GetValue(from) is { } exportFile)
            {
                if (!File.Exists(exportFile))
                {
                    Terminal.Error($"{Path.GetFullPath(exportFile)} does not exist.");
                    return ExitCodes.ConfigurationError;
                }

                var rows = HtmlReport.FromExport(await File.ReadAllTextAsync(exportFile, cancellationToken));
                if (!rows.IsSuccess)
                {
                    Terminal.Error($"{Path.GetFullPath(exportFile)}: {rows.Error}");
                    return ExitCodes.ConfigurationError;
                }

                return await WriteAsync(destination, rows.Value, TimeProvider.System.GetUtcNow());
            }

            return await LapseRuntime.RunAsync(parse, async runtime =>
            {
                var now = runtime.Time.GetUtcNow();
                var items = await runtime.Store.GetItemsAsync(cancellationToken);
                var rows = ItemRows.ForItems(items.Values, runtime.Plan.Policy, now).ToList();
                return await WriteAsync(destination, [.. HtmlReport.FromItems(rows)], now);
            }, cancellationToken);
        });
        return command;
    }

    private static async Task<int> WriteAsync(string destination, IReadOnlyList<ReportRow> rows, DateTimeOffset now)
    {
        await using (var file = File.Create(destination))
        {
            HtmlReport.Write(file, rows, now);
        }

        Terminal.Line($"Created the report ({Terminal.Count(rows.Count, "item", "items")}): {destination}");
        Terminal.Line("Open it in your browser. It works offline and loads nothing from the internet.");
        return ExitCodes.Success;
    }
}
