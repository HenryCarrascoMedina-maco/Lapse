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
    public static Reconciliation Reconcile(Item? existing, WatchTarget target, Result<Observation> result, DateTimeOffset now)
    {
        var previousExpiry = existing?.ExpiresAt;

        if (!result.IsSuccess)
        {
            var failing = new Item(
                target.Key,
                existing?.Name ?? target.Name,
                target.Owner,
                previousExpiry,
                ItemStatus.Failing,
                now,
                result.Error);
            return new Reconciliation(failing, ScanOutcome.Failed, previousExpiry, Observation: null);
        }

        var observation = result.Value;
        var outcome = OutcomeOf(previousExpiry, observation.ExpiresAt);
        var item = new Item(
            target.Key,
            observation.Name,
            target.Owner,
            observation.ExpiresAt,
            outcome == ScanOutcome.Renewed ? ItemStatus.Renewed : ItemStatus.Active,
            now,
            LastError: null);
        return new Reconciliation(item, outcome, previousExpiry, observation);
    }

    private static ScanOutcome OutcomeOf(DateTimeOffset? previous, DateTimeOffset current) => previous switch
    {
        null => ScanOutcome.New,
        var earlier when current > earlier => ScanOutcome.Renewed,
        var later when current < later => ScanOutcome.Changed,
        _ => ScanOutcome.Unchanged,
    };
}
