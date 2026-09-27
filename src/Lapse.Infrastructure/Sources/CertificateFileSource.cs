using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Certificates;

namespace Lapse.Infrastructure.Sources;

public sealed class CertificateFileSource : ISource
{
    public ItemKind Kind => ItemKind.File;

    public Task<Result<Observation>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken) =>
        CertificateReader.ObserveFileAsync(target.Key.Value, cancellationToken);
}
