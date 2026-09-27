using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;

namespace Lapse.Infrastructure.Sources;

public sealed class ManualSource : ISource
{
    public ItemKind Kind => ItemKind.Manual;

    public Task<Result<Observation>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken) =>
        Task.FromResult(target.DeclaredExpiresAt is { } expiresAt
            ? Result.Success(new Observation(target.Key.Value, expiresAt, Fingerprint: null))
            : Result.Failure<Observation>("The manual item has no expiry date."));
}
