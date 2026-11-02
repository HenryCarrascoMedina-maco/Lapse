using Lapse.Core.Scanning;

namespace Lapse.Core.Alerts;

public static class AlertPlanner
{
    // Slightly under a day, so a scan scheduled every 6 or 24 hours that starts a few seconds early still counts.
    public static readonly TimeSpan UnverifiedAfter = TimeSpan.FromHours(23);

    // renewedFrom is the expiry date observed before the current one, when someone was warned about it.
    // It comes from the store, so the renewal is announced whichever command first saw the new date.
    public static IReadOnlyList<PlannedAlert> Plan(Reconciliation reconciliation, WatchPlan plan, DateTimeOffset now, DateTimeOffset? renewedFrom = null)
    {
        var item = reconciliation.Item;
        var verified = reconciliation.Outcome != ScanOutcome.Failed;
        var alerts = new List<PlannedAlert>();

        if (!verified && item.FailingSince is { } failingSince && now - failingSince >= UnverifiedAfter)
        {
            alerts.Add(Create(AlertKind.Unverified, thresholdDays: 0, daysRemaining: null, includeBackup: false));
        }

        if (item.ExpiresAt is not { } expiresAt)
        {
            return alerts;
        }

        var days = AlertPolicy.DaysRemaining(expiresAt, now);

        if (verified && renewedFrom < expiresAt)
        {
            alerts.Add(Create(AlertKind.Renewed, thresholdDays: 0, days, includeBackup: false, renewedFrom));
        }

        if (verified && item.Problem is not null)
        {
            alerts.Add(Create(AlertKind.Problem, thresholdDays: 0, days, includeBackup: false));
        }

        // A failed scan keeps the last observed date, so the countdown goes on while Lapse cannot reach the item.
        if (expiresAt <= now)
        {
            alerts.Add(Create(AlertKind.Expired, thresholdDays: 0, days, includeBackup: true));
        }
        else if (plan.Policy.TightestThresholdReached(days) is { } threshold)
        {
            alerts.Add(Create(AlertKind.Threshold, threshold, days, includeBackup: days <= plan.Policy.CriticalDays));
        }

        return alerts;

        PlannedAlert Create(AlertKind kind, int thresholdDays, int? daysRemaining, bool includeBackup, DateTimeOffset? previousExpiresAt = null) => new(
            item,
            kind,
            thresholdDays,
            daysRemaining,
            plan.RecipientsFor(item.Owner, includeBackup),
            previousExpiresAt);
    }
}
