using System.Globalization;
using System.Text.Json;
using Lapse.Core;

namespace Lapse.Infrastructure.Rdap;

internal static class RdapParser
{
    public static Result<IReadOnlyDictionary<string, Uri>> ParseBootstrap(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var servers = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);

            foreach (var service in document.RootElement.GetProperty("services").EnumerateArray())
            {
                var server = service[1].EnumerateArray()
                    .Select(url => url.GetString())
                    .FirstOrDefault(url => url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true);

                if (server is null)
                {
                    continue;
                }

                var baseUri = new Uri(server.EndsWith('/') ? server : server + "/");
                foreach (var tld in service[0].EnumerateArray())
                {
                    servers.TryAdd(tld.GetString()!, baseUri);
                }
            }

            return Result.Success<IReadOnlyDictionary<string, Uri>>(servers);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return Result.Failure<IReadOnlyDictionary<string, Uri>>("The IANA RDAP directory does not have the expected format.");
        }
    }

    public static Result<DateTimeOffset> ParseExpiration(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("events", out var events))
            {
                return NoExpiration();
            }

            var expiration = events.EnumerateArray()
                .Where(e => e.TryGetProperty("eventAction", out var action) && action.GetString() == "expiration")
                .Select(e => e.GetProperty("eventDate").GetString())
                .FirstOrDefault();

            return expiration is null
                ? NoExpiration()
                : Result.Success(DateTimeOffset.Parse(expiration, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime());
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return Result.Failure<DateTimeOffset>("the RDAP response does not have the expected format");
        }
    }

    private static Result<DateTimeOffset> NoExpiration() =>
        Result.Failure<DateTimeOffset>("the registry does not publish an expiration date; declare it as a manual item");
}
