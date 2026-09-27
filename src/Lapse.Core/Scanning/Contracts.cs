using Lapse.Core.Alerts;
using Lapse.Core.Items;

namespace Lapse.Core.Scanning;

public interface ISource
{
    ItemKind Kind { get; }

    Task<Result<Observation>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken);
}

public interface IItemStore
{
    Task<IReadOnlyDictionary<ItemKey, Item>> GetItemsAsync(CancellationToken cancellationToken);

    Task ReplaceInventoryAsync(IReadOnlyList<Reconciliation> reconciliations, CancellationToken cancellationToken);

    Task<bool> WasAlertSentAsync(AlertRecord record, CancellationToken cancellationToken);

    Task RecordAlertSentAsync(AlertRecord record, DateTimeOffset sentAt, CancellationToken cancellationToken);
}
