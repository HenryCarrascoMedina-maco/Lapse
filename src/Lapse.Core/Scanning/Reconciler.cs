using Lapse.Core.Items;

namespace Lapse.Core.Scanning;

public enum ScanOutcome
{
    New,
    Unchanged,
    Renewed,
    Changed,
    Failed,
}

public sealed record Reconciliation(
    Item Item,
    ScanOutcome Outcome,
    DateTimeOffset? PreviousExpiresAt,
    Observation? Observation);

public static class Reconciler
{
    public static IReadOnlyList<Reconciliation> Reconcile(
        IReadOnlyCollection<Item> existing,
        WatchTarget target,
        Result<IReadOnlyList<Observation>> result,
        DateTimeOffset now,
        Func<Observation, string>? ownerOf = null)
    {
        if (!result.IsSuccess)
        {
            var known = existing.Count > 0 ? existing : [Placeholder(target, now)];
            return [.. known.Select(item => Failed(item, result.Error, now))];
        }

        var previous = existing.ToDictionary(item => item.Key);
        return [.. result.Value.Select(observation =>
            Observed(previous.GetValueOrDefault(observation.Key), target, observation, ownerOf?.Invoke(observation) ?? target.Owner, now))];
    }

    private static Item Placeholder(WatchTarget target, DateTimeOffset now) =>
        new(target.Key, target.Key, target.Name, target.Owner, ExpiresAt: null, ItemStatus.Failing, now, LastError: null);

    private static Reconciliation Failed(Item item, string error, DateTimeOffset now) => new(
        item with { Status = ItemStatus.Failing, LastScannedAt = now, LastError = error },
        ScanOutcome.Failed,
        item.ExpiresAt,
        Observation: null);

    private static Reconciliation Observed(Item? previous, WatchTarget target, Observation observation, string owner, DateTimeOffset now)
    {
        var outcome = OutcomeOf(previous?.ExpiresAt, observation.ExpiresAt);
        var item = new Item(
            observation.Key,
            target.Key,
            observation.Name,
            owner,
            observation.ExpiresAt,
            outcome == ScanOutcome.Renewed ? ItemStatus.Renewed : ItemStatus.Active,
            now,
            LastError: null);
        return new Reconciliation(item, outcome, previous?.ExpiresAt, observation);
    }

    private static ScanOutcome OutcomeOf(DateTimeOffset? previous, DateTimeOffset current) => previous switch
    {
        null => ScanOutcome.New,
        var earlier when current > earlier => ScanOutcome.Renewed,
        var later when current < later => ScanOutcome.Changed,
        _ => ScanOutcome.Unchanged,
    };
}
