using System.CommandLine;
using Lapse.Cli.Output;

namespace Lapse.Cli.Commands;

internal static class ListCommand
{
    public static Command Create()
    {
        var json = GlobalOptions.Json();
        var command = new Command("list", "Show the inventory stored by the last scan, without connecting to anything.") { json };
        command.SetAction((parse, cancellationToken) => LapseRuntime.RunAsync(parse, async runtime =>
        {
            var now = runtime.Time.GetUtcNow();
            var items = await runtime.Store.GetItemsAsync(cancellationToken);
            var rows = ItemRows.ForItems(items.Values, runtime.Plan.Policy, now).ToList();

            if (parse.GetValue(json))
            {
                ItemRows.WriteDocumentToOutput(writer => ItemRows.WriteJson(writer, rows, now));
            }
            else if (rows.Count == 0)
            {
                Terminal.Line("No data yet. Run: lapse scan");
            }
            else
            {
                ItemRows.WriteTable(rows, now);
            }

            return ExitCodes.Success;
        }, cancellationToken));
        return command;
    }
}
