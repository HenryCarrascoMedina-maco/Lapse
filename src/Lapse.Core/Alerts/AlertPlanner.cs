using Lapse.Core.Scanning;

namespace Lapse.Core.Alerts;

public static class AlertPlanner
{
    public static PlannedAlert? Plan(Reconciliation reconciliation, WatchPlan plan, DateTimeOffset now)
    {
        var item = reconciliation.Item;
        if (reconciliation.Outcome == ScanOutcome.Failed || item.ExpiresAt is not { } expiresAt)
        {
            return null;
        }

        var days = AlertPolicy.DaysRemaining(expiresAt, now);

        if (reconciliation.Outcome == ScanOutcome.Renewed)
        {
            return Create(AlertKind.Renewed, thresholdDays: 0, includeBackup: false);
        }

        if (expiresAt <= now)
        {
            return Create(AlertKind.Expired, thresholdDays: 0, includeBackup: true);
        }

        return plan.Policy.TightestThresholdReached(days) is { } threshold
            ? Create(AlertKind.Threshold, threshold, includeBackup: days <= plan.Policy.CriticalDays)
            : null;

        PlannedAlert Create(AlertKind kind, int thresholdDays, bool includeBackup) => new(
            item,
            kind,
            thresholdDays,
            days,
            expiresAt,
            plan.RecipientsFor(item.Owner, includeBackup),
            reconciliation.PreviousExpiresAt);
    }
}
