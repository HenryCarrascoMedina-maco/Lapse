using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lapse.Infrastructure.Configuration;

internal sealed class ConfigDocument
{
    public IReadOnlyDictionary<string, OwnerDocument>? Owners { get; init; }

    public DefaultsDocument? Defaults { get; init; }

    public WatchDocument? Watch { get; init; }

    public NotifyDocument? Notify { get; init; }
}

internal sealed class OwnerDocument
{
    public string? Email { get; init; }

    public string? Backup { get; init; }
}

internal sealed class DefaultsDocument
{
    public string? Owner { get; init; }

    public IReadOnlyList<int>? WarnAtDays { get; init; }

    public int? CriticalDays { get; init; }
}

internal sealed class WatchDocument
{
    [JsonConverter(typeof(TargetListConverter))]
    public IReadOnlyList<TargetDocument>? Hosts { get; init; }

    [JsonConverter(typeof(TargetListConverter))]
    public IReadOnlyList<TargetDocument>? Domains { get; init; }

    [JsonConverter(typeof(TargetListConverter))]
    public IReadOnlyList<TargetDocument>? Files { get; init; }

    [JsonConverter(typeof(TargetListConverter))]
    public IReadOnlyList<TargetDocument>? Manual { get; init; }

    [JsonConverter(typeof(TargetListConverter))]
    public IReadOnlyList<TargetDocument>? Saml { get; init; }

    public IReadOnlyList<EntraDocument>? Entra { get; init; }
}

internal sealed class EntraDocument
{
    public string? TenantId { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? Name { get; init; }

    public string? Owner { get; init; }
}

internal sealed class TargetDocument
{
    public string? Target { get; init; }

    public string? Path { get; init; }

    public string? Name { get; init; }

    public string? Owner { get; init; }

    public string? ExpiresAt { get; init; }
}

internal sealed class NotifyDocument
{
    public EmailDocument? Email { get; init; }

    public WebhookDocument? Webhook { get; init; }

    public WebhookDocument? Teams { get; init; }

    public TelegramDocument? Telegram { get; init; }
}

internal sealed class TelegramDocument
{
    public string? BotToken { get; init; }

    public string? ChatId { get; init; }
}

internal sealed class EmailDocument
{
    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? User { get; init; }

    public string? Password { get; init; }

    public string? From { get; init; }

    public bool? UseTls { get; init; }
}

internal sealed class WebhookDocument
{
    public string? Url { get; init; }
}

internal sealed class TargetListConverter : JsonConverter<IReadOnlyList<TargetDocument>>
{
    public override IReadOnlyList<TargetDocument> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected a list.");
        }

        var targets = new List<TargetDocument>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            targets.Add(reader.TokenType switch
            {
                JsonTokenType.String => new TargetDocument { Target = reader.GetString() },
                JsonTokenType.StartObject => JsonSerializer.Deserialize(ref reader, ConfigJsonContext.Default.TargetDocument)!,
                _ => throw new JsonException("Each entry must be a string or an object."),
            });
        }

        return targets;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<TargetDocument> value, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ConfigDocument))]
[JsonSerializable(typeof(TargetDocument))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;
