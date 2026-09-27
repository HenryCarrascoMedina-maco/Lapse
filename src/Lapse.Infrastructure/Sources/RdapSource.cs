using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Http;
using Lapse.Infrastructure.Rdap;

namespace Lapse.Infrastructure.Sources;

public sealed class RdapSource(HttpClient http, RdapBootstrap bootstrap) : ISource
{
    public ItemKind Kind => ItemKind.Domain;

    public async Task<Result<Observation>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken)
    {
        var domain = target.Key.Value;
        var server = await bootstrap.FindServerAsync(domain);
        if (!server.IsSuccess)
        {
            return Result.Failure<Observation>(server.Error);
        }

        var response = await HttpFetch.GetStringAsync(http, new Uri(server.Value, $"domain/{domain}"), cancellationToken);
        return response
            .Bind(RdapParser.ParseExpiration)
            .Map(expiresAt => new Observation(domain, expiresAt, Fingerprint: null));
    }
}
