using System.Globalization;
using System.Text.Json;
using Lapse.Core;
using Lapse.Core.Items;

namespace Lapse.Infrastructure.Entra;

public sealed record EntraPage(IReadOnlyList<Observation> Items, Uri? Next);

internal static class EntraGraph
{
    public static Result<string> ParseToken(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("access_token", out var token) && token.GetString() is { Length: > 0 } value
                ? Result.Success(value)
                : Result.Failure<string>("Entra did not return an access token");
        }
        catch (JsonException)
        {
            return Result.Failure<string>("Entra returned an unexpected token response");
        }
    }

    public static Result<EntraPage> ParseApplications(string json, string tenantId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var items = new List<Observation>();

            foreach (var application in document.RootElement.GetProperty("value").EnumerateArray())
            {
                var appId = application.GetProperty("appId").GetString()!;
                var appName = application.TryGetProperty("displayName", out var name) ? name.GetString() ?? appId : appId;
                var contacts = Contacts(application);

                items.AddRange(Credentials(application, "passwordCredentials", "secret", tenantId, appId, appName, contacts));
                items.AddRange(Credentials(application, "keyCredentials", "certificate", tenantId, appId, appName, contacts));
            }

            var next = document.RootElement.TryGetProperty("@odata.nextLink", out var link) && link.GetString() is { } url
                ? new Uri(url)
                : null;

            return Result.Success(new EntraPage(items, next));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UriFormatException)
        {
            return Result.Failure<EntraPage>("Microsoft Graph returned an unexpected response");
        }
    }

    private static IEnumerable<Observation> Credentials(
        JsonElement application,
        string property,
        string kind,
        string tenantId,
        string appId,
        string appName,
        IReadOnlyList<string> contacts)
    {
        if (!application.TryGetProperty(property, out var credentials) || credentials.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var credential in credentials.EnumerateArray())
        {
            if (credential.GetProperty("endDateTime").GetString() is not { } end)
            {
                continue;
            }

            var keyId = credential.GetProperty("keyId").GetString()!;
            var label = credential.TryGetProperty("displayName", out var display) && display.GetString() is { Length: > 0 } text
                ? text
                : keyId[..Math.Min(8, keyId.Length)];

            yield return new Observation(
                new ItemKey(ItemKind.Entra, $"{tenantId}/{appId}/{kind}/{keyId}"),
                $"{appName} · {kind} {label}",
                DateTimeOffset.Parse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                Fingerprint: null,
                contacts);
        }
    }

    private static List<string> Contacts(JsonElement application)
    {
        if (!application.TryGetProperty("owners", out var owners) || owners.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. owners.EnumerateArray()
            .Select(owner => Text(owner, "mail") ?? Text(owner, "userPrincipalName"))
            .OfType<string>()];
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
