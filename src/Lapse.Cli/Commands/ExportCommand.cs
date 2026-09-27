using System.CommandLine;
using Lapse.Cli.Output;

namespace Lapse.Cli.Commands;

internal static class ExportCommand
{
    public static Command Create()
    {
        var output = new Option<string>("--output", "-o")
        {
            Description = "Destination JSON file.",
            Required = true,
        };

        var command = new Command("export", "Export the inventory to a JSON file, for example as audit evidence.") { output };
        command.SetAction((parse, cancellationToken) => LapseRuntime.RunAsync(parse, async runtime =>
        {
            var now = runtime.Time.GetUtcNow();
            var items = await runtime.Store.GetItemsAsync(cancellationToken);
            var rows = ItemRows.ForItems(items.Values, runtime.Plan.Policy, now).ToList();
            var path = Path.GetFullPath(parse.GetRequiredValue(output));

            await using (var file = File.Create(path))
            {
                ItemRows.WriteDocument(file, writer => ItemRows.WriteJson(writer, rows, now));
            }

            Terminal.Line($"Exported the inventory ({Terminal.Count(rows.Count, "item", "items")}) to {path}");
            return ExitCodes.Success;
        }, cancellationToken));
        return command;
    }
}
