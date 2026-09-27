using System.CommandLine;
using Lapse.Cli.Commands;

namespace Lapse.Cli;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int CheckFailed = 1;
    public const int ConfigurationError = 2;
    public const int DeliveryFailed = 3;
    public const int ExternalServiceError = 4;
}

internal static class GlobalOptions
{
    public static readonly Option<string> Config = new("--config", "-c")
    {
        Description = "Configuration file.",
        DefaultValueFactory = _ => "lapse.json",
        Recursive = true,
    };

    public static readonly Option<string?> Database = new("--db")
    {
        Description = "Local database. Defaults to lapse.db next to the configuration file.",
        Recursive = true,
    };

    public static Option<bool> Json() => new("--json") { Description = "Write the result as JSON." };
}

internal static class LapseCommands
{
    public static RootCommand Create()
    {
        var root = new RootCommand("Lapse watches certificates, domains and certificate files, and warns before they expire.");
        root.Options.Add(GlobalOptions.Config);
        root.Options.Add(GlobalOptions.Database);

        root.Subcommands.Add(InitCommand.Create());
        root.Subcommands.Add(ScanCommand.Create());
        root.Subcommands.Add(CheckCommand.Create());
        root.Subcommands.Add(ListCommand.Create());
        root.Subcommands.Add(ExportCommand.Create());
        root.Subcommands.Add(DiscoverCommand.Create());
        root.Subcommands.Add(WatchCommand.Create());
        return root;
    }
}
