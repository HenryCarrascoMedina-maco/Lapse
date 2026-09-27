using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Certificates;

namespace Lapse.Infrastructure.Sources;

public sealed class TlsSource(TimeSpan timeout) : ISource
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public ItemKind Kind => ItemKind.Tls;

    public async Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken)
    {
        var endpoint = HostEndpoint.Parse(target.Key.Value);
        var certificate = await TlsProbe.FetchCertificateAsync(endpoint, timeout, cancellationToken);
        return certificate.Bind(content => CertificateReader.Observe(target.Key, target.Key.Value, content)).AsList();
    }
}
