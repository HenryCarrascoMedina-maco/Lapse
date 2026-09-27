using System.Globalization;

namespace Lapse.Infrastructure.Sources;

public sealed record HostEndpoint(string Host, int Port)
{
    public const int DefaultPort = 443;

    public static bool TryParse(string? value, out HostEndpoint endpoint)
    {
        endpoint = new HostEndpoint(string.Empty, DefaultPort);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var separator = text.LastIndexOf(':');
        var host = separator < 0 ? text : text[..separator];
        var port = DefaultPort;

        if (separator >= 0 && !int.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port))
        {
            return false;
        }

        if (port is < 1 or > 65535 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            return false;
        }

        endpoint = new HostEndpoint(host.ToLowerInvariant(), port);
        return true;
    }

    public static HostEndpoint Parse(string value) =>
        TryParse(value, out var endpoint) ? endpoint : throw new FormatException($"'{value}' is not in host:port form.");

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");
}
