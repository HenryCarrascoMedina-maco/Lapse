using System.Net;
using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Http;
using Lapse.Infrastructure.Rdap;

namespace Lapse.Infrastructure.Sources;

public sealed class RdapSource(HttpClient http, RdapBootstrap bootstrap) : ISource
{
    public ItemKind Kind => ItemKind.Domain;

    public async Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken)
    {
        var domain = target.Key.Value;
        var server = await bootstrap.FindServerAsync(domain);
        if (!server.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<Observation>>(server.Error);
        }

        var response = await HttpFetch.GetStringAsync(
            http,
            new Uri(server.Value, $"domain/{domain}"),
            status => DescribeFailure(domain, status),
            cancellationToken);

        return response
            .Bind(RdapParser.ParseExpiration)
            .Map(expiresAt => new Observation(target.Key, domain, expiresAt, Fingerprint: null))
            .AsList();
    }

    private static string? DescribeFailure(string domain, HttpStatusCode status)
    {
        if (status is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound))
        {
            return null;
        }

        var labels = domain.Split('.');
        return labels.Length > 2
            ? $"the registry has no domain named {domain}; if it is a subdomain, watch its registered domain instead, such as {string.Join('.', labels[1..])}"
            : $"the registry has no domain named {domain}";
    }
}
