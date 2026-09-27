using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Certificates;
using Lapse.Infrastructure.Http;
using Lapse.Infrastructure.Saml;

namespace Lapse.Infrastructure.Sources;

public sealed class SamlSource(HttpClient http) : ISource
{
    public ItemKind Kind => ItemKind.Saml;

    public async Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken)
    {
        var url = new Uri(target.Key.Value);
        var metadata = (await HttpFetch.GetStringAsync(http, url, cancellationToken)).Bind(SamlMetadata.ParseCertificates);
        if (!metadata.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<Observation>>(metadata.Error);
        }

        var observations = new List<Observation>();
        foreach (var certificate in metadata.Value)
        {
            var observed = CertificateReader.Observe(target.Key, $"{url.Host} · {certificate.Use} certificate", certificate.Content);
            if (!observed.IsSuccess)
            {
                return Result.Failure<IReadOnlyList<Observation>>(observed.Error);
            }

            var fingerprint = observed.Value.Fingerprint!;
            observations.Add(observed.Value with
            {
                Key = new ItemKey(ItemKind.Saml, $"{target.Key.Value}#{fingerprint}"),
                Name = $"{observed.Value.Name} {fingerprint[..8]}",
            });
        }

        return Result.Success<IReadOnlyList<Observation>>(observations);
    }
}
