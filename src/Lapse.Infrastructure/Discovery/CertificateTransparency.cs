using System.Globalization;
using System.Text.Json;
using Lapse.Core;
using Lapse.Infrastructure.Http;

namespace Lapse.Infrastructure.Discovery;

public sealed record DiscoveredName(string Name, DateTimeOffset LatestExpiry, string Issuer)
{
    public bool IsWildcard => Name.StartsWith("*.", StringComparison.Ordinal);
}

public sealed class CertificateTransparency(HttpClient http)
{
    public async Task<Result<IReadOnlyList<DiscoveredName>>> SearchAsync(string domain, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://crt.sh/?q=%25.{Uri.EscapeDataString(domain)}&output=json&exclude=expired");
        var response = await HttpFetch.GetStringAsync(http, uri, cancellationToken);
        return response.Bind(json => Parse(json, domain));
    }

    internal static Result<IReadOnlyList<DiscoveredName>> Parse(string json, string domain)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var latest = new Dictionary<string, DiscoveredName>(StringComparer.Ordinal);

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var expiry = DateTimeOffset.Parse(
                    entry.GetProperty("not_after").GetString()!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal).ToUniversalTime();
                var issuer = entry.GetProperty("issuer_name").GetString() ?? string.Empty;

                var names = (entry.GetProperty("name_value").GetString() ?? string.Empty)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(name => name.ToLowerInvariant())
                    .Where(name => BelongsTo(name, domain));

                foreach (var name in names)
                {
                    if (!latest.TryGetValue(name, out var known) || expiry > known.LatestExpiry)
                    {
                        latest[name] = new DiscoveredName(name, expiry, IssuerCommonName(issuer));
                    }
                }
            }

            return Result.Success<IReadOnlyList<DiscoveredName>>([.. latest.Values.OrderBy(found => found.Name, StringComparer.Ordinal)]);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return Result.Failure<IReadOnlyList<DiscoveredName>>("crt.sh returned a response with an unexpected format");
        }
    }

    private static bool BelongsTo(string name, string domain) =>
        name == domain || name.EndsWith("." + domain, StringComparison.Ordinal);

    private static string IssuerCommonName(string issuer) =>
        issuer.Split(',', StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith("CN=", StringComparison.Ordinal))?[3..]
        ?? issuer;
}
