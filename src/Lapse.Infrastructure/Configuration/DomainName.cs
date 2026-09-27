using System.Globalization;
using Lapse.Core;

namespace Lapse.Infrastructure.Configuration;

public static class DomainName
{
    public static Result<string> Normalize(string? value)
    {
        var text = value?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(text) || !text.Contains('.', StringComparison.Ordinal))
        {
            return Result.Failure<string>("expected a domain, for example \"acme.com\"");
        }

        if (!text.All(char.IsAscii))
        {
            try
            {
                text = new IdnMapping().GetAscii(text);
            }
            catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
            {
                return Result.Failure<string>("invalid domain; write it in its xn-- form");
            }
        }

        return Uri.CheckHostName(text) == UriHostNameType.Dns
            ? Result.Success(text)
            : Result.Failure<string>("invalid domain");
    }
}
