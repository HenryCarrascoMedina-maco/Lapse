using System.Globalization;

namespace Lapse.Infrastructure.Sources;

public enum TlsProtocol
{
    Direct,
    Smtp,
    Imap,
    Pop3,
    Postgres,
}

public sealed record HostEndpoint(string Host, int Port, TlsProtocol Protocol = TlsProtocol.Direct)
{
    public const int DefaultPort = 443;

    private const string SchemeSeparator = "://";

    private static readonly Dictionary<string, (TlsProtocol Protocol, int Port)> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["smtp"] = (TlsProtocol.Smtp, 587),
        ["imap"] = (TlsProtocol.Imap, 143),
        ["pop3"] = (TlsProtocol.Pop3, 110),
        ["postgres"] = (TlsProtocol.Postgres, 5432),
    };

    public static bool TryParse(string? value, out HostEndpoint endpoint)
    {
        endpoint = new HostEndpoint(string.Empty, DefaultPort);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var (protocol, port) = (TlsProtocol.Direct, DefaultPort);

        var schemeEnd = text.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            if (!Schemes.TryGetValue(text[..schemeEnd], out var scheme))
            {
                return false;
            }

            (protocol, port) = scheme;
            text = text[(schemeEnd + SchemeSeparator.Length)..];
        }

        var separator = text.LastIndexOf(':');
        var host = separator < 0 ? text : text[..separator];

        if (separator >= 0 && !int.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port))
        {
            return false;
        }

        if (port is < 1 or > 65535 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            return false;
        }

        endpoint = new HostEndpoint(host.ToLowerInvariant(), port, protocol);
        return true;
    }

    public static HostEndpoint Parse(string value) =>
        TryParse(value, out var endpoint) ? endpoint : throw new FormatException($"'{value}' is not a valid TLS endpoint.");

    public override string ToString() => Protocol == TlsProtocol.Direct
        ? string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}")
        : string.Create(CultureInfo.InvariantCulture, $"{Protocol.ToString().ToLowerInvariant()}{SchemeSeparator}{Host}:{Port}");
}
