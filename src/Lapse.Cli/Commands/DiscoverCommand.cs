using System.CommandLine;
using Lapse.Cli.Output;
using Lapse.Core.Items;
using Lapse.Infrastructure.Configuration;
using Lapse.Infrastructure.Discovery;
using Lapse.Infrastructure.Sources;

namespace Lapse.Cli.Commands;

internal static class DiscoverCommand
{
    public static Command Create()
    {
        var domain = new Argument<string>("domain") { Description = "Domain to look up, for example acme.com." };
        var json = GlobalOptions.Json();

        var command = new Command("discover", "Search Certificate Transparency (crt.sh) for certificates of your domain that you are not watching.")
        {
            domain,
            json,
        };

        command.SetAction((parse, cancellationToken) => LapseRuntime.RunAsync(parse, async runtime =>
        {
            var normalized = DomainName.Normalize(parse.GetValue(domain));
            if (!normalized.IsSuccess)
            {
                Terminal.Error(normalized.Error);
                return ExitCodes.ConfigurationError;
            }

            var found = await new CertificateTransparency(runtime.Http).SearchAsync(normalized.Value, cancellationToken);
            if (!found.IsSuccess)
            {
                Terminal.Error($"Could not query crt.sh: {found.Error}");
                return ExitCodes.ExternalServiceError;
            }

            var watchedHosts = runtime.Plan.Targets
                .Where(target => target.Key.Kind == ItemKind.Tls)
                .Select(target => HostEndpoint.Parse(target.Key.Value).Host)
                .ToHashSet(StringComparer.Ordinal);

            if (parse.GetValue(json))
            {
                WriteJson(found.Value, watchedHosts);
            }
            else
            {
                WriteTable(found.Value, watchedHosts, normalized.Value);
            }

            return ExitCodes.Success;
        }, cancellationToken));
        return command;
    }

    private static void WriteTable(IReadOnlyList<DiscoveredName> names, HashSet<string> watchedHosts, string domain)
    {
        if (names.Count == 0)
        {
            Terminal.Line($"crt.sh has no valid certificates for {domain}.");
            return;
        }

        var rows = names.Select(name => (IReadOnlyList<Cell>)
        [
            new Cell(name.Name),
            new Cell(ItemRows.FormatDate(name.LatestExpiry)),
            new Cell(name.Issuer),
            WatchedCell(name, watchedHosts),
        ]).ToList();

        ConsoleTable.Write(["NAME", "EXPIRES", "ISSUER", "WATCHED"], rows);

        var unwatched = names.Count(name => !name.IsWildcard && !watchedHosts.Contains(name.Name));
        Terminal.Line();
        Terminal.Line(unwatched == 0
            ? "Every name found is already watched."
            : $"{Terminal.Count(unwatched, "name is", "names are")} not watched. To watch them, add them to watch.hosts as \"name:443\".");
    }

    private static Cell WatchedCell(DiscoveredName name, HashSet<string> watchedHosts) =>
        name.IsWildcard ? new Cell("wildcard", Tone.Muted)
        : watchedHosts.Contains(name.Name) ? new Cell("yes", Tone.Good)
        : new Cell("no", Tone.Warning);

    private static void WriteJson(IReadOnlyList<DiscoveredName> names, HashSet<string> watchedHosts) =>
        ItemRows.WriteDocumentToOutput(writer =>
        {
            writer.WriteStartArray();
            foreach (var name in names)
            {
                writer.WriteStartObject();
                writer.WriteString("name", name.Name);
                writer.WriteString("latestExpiry", name.LatestExpiry);
                writer.WriteString("issuer", name.Issuer);
                writer.WriteBoolean("wildcard", name.IsWildcard);
                writer.WriteBoolean("watched", watchedHosts.Contains(name.Name));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
}
