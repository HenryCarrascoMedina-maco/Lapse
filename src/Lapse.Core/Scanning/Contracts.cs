using Lapse.Core.Alerts;
using Lapse.Core.Items;

namespace Lapse.Core.Scanning;

public interface ISource
{
    ItemKind Kind { get; }

    Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken);
}

public interface IItemStore
{
    Task<IReadOnlyDictionary<ItemKey, Item>> GetItemsAsync(CancellationToken cancellationToken);

    Task ReplaceInventoryAsync(IReadOnlyList<Reconciliation> reconciliations, CancellationToken cancellationToken);

    Task<bool> WasAlertSentAsync(AlertRecord record, CancellationToken cancellationToken);

    Task RecordAlertSentAsync(AlertRecord record, DateTimeOffset sentAt, CancellationToken cancellationToken);

    // The expiry date observed before currentExpiry, if it was earlier and a threshold or expired alert was sent for it.
    Task<DateTimeOffset?> GetRenewedFromAsync(ItemKey key, DateTimeOffset currentExpiry, CancellationToken cancellationToken);
}

public static class ObservationResults
{
    public static Result<IReadOnlyList<Observation>> AsList(this Result<Observation> result) =>
        result.Map<IReadOnlyList<Observation>>(observation => [observation]);
}
