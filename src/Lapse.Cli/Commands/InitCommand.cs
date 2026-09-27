using System.CommandLine;
using Lapse.Cli.Output;

namespace Lapse.Cli.Commands;

internal static class InitCommand
{
    private const string TemplateResource = "Lapse.lapse.example.json";

    public static Command Create()
    {
        var force = new Option<bool>("--force") { Description = "Replace the file if it already exists." };
        var command = new Command("init", "Create an example configuration file.") { force };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var path = Path.GetFullPath(parse.GetRequiredValue(GlobalOptions.Config));
            if (File.Exists(path) && !parse.GetValue(force))
            {
                Terminal.Error($"{path} already exists. Use --force to replace it.");
                return ExitCodes.ConfigurationError;
            }

            await using (var template = typeof(InitCommand).Assembly.GetManifestResourceStream(TemplateResource)!)
            await using (var file = File.Create(path))
            {
                await template.CopyToAsync(file, cancellationToken);
            }

            Terminal.Line($"Created {path}");
            Terminal.Line();
            Terminal.Line("Next steps:");
            Terminal.Line("  1. Edit the file: owners, hosts, domains and files to watch.");
            Terminal.Line("  2. lapse discover your-domain.com   (certificates you are not watching yet)");
            Terminal.Line("  3. lapse scan");
            return ExitCodes.Success;
        });
        return command;
    }
}
