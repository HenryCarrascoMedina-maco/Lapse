using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Certificates;

namespace Lapse.Infrastructure.Sources;

public sealed class CertificateFileSource : ISource
{
    public ItemKind Kind => ItemKind.File;

    public async Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken) =>
        (await CertificateReader.ObserveFileAsync(target.Key, cancellationToken)).AsList();
}
