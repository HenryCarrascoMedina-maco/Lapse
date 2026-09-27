using Lapse.Core.Items;

namespace Lapse.Core.Alerts;

public enum Urgency
{
    Unknown,
    Ok,
    Warning,
    Critical,
    Expired,
}

public enum ItemHealth
{
    Unknown,
    Ok,
    Renewed,
    Warning,
    Critical,
    Expired,
    Error,
}

public sealed class AlertPolicy
{
    public const int DefaultCriticalDays = 7;

    public static readonly IReadOnlyList<int> DefaultWarnAtDays = [30, 14, 7, 1];

    public AlertPolicy(IReadOnlyList<int> warnAtDays, int criticalDays = DefaultCriticalDays)
    {
        if (warnAtDays.Count == 0 || warnAtDays.Any(days => days <= 0))
        {
            throw new ArgumentException("Warning thresholds must be positive numbers of days.", nameof(warnAtDays));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(criticalDays);

        WarnAtDays = [.. warnAtDays.Distinct().OrderDescending()];
        CriticalDays = criticalDays;
    }

    public static AlertPolicy Default { get; } = new(DefaultWarnAtDays);

    public IReadOnlyList<int> WarnAtDays { get; }

    public int CriticalDays { get; }

    public static int DaysRemaining(DateTimeOffset expiresAt, DateTimeOffset now) => (int)(expiresAt - now).TotalDays;

    public int? TightestThresholdReached(int daysRemaining) =>
        WarnAtDays.Where(threshold => daysRemaining <= threshold).Cast<int?>().LastOrDefault();

    public Urgency UrgencyOf(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is not { } expiry)
        {
            return Urgency.Unknown;
        }

        if (expiry <= now)
        {
            return Urgency.Expired;
        }

        var days = DaysRemaining(expiry, now);
        return days <= CriticalDays ? Urgency.Critical
            : days <= WarnAtDays[0] ? Urgency.Warning
            : Urgency.Ok;
    }

    public ItemHealth HealthOf(Item item, DateTimeOffset now) => (item.Status, UrgencyOf(item.ExpiresAt, now)) switch
    {
        (ItemStatus.Failing, _) => ItemHealth.Error,
        (_, Urgency.Expired) => ItemHealth.Expired,
        (_, Urgency.Critical) => ItemHealth.Critical,
        (ItemStatus.Renewed, _) => ItemHealth.Renewed,
        (_, Urgency.Warning) => ItemHealth.Warning,
        (_, Urgency.Ok) => ItemHealth.Ok,
        _ => ItemHealth.Unknown,
    };
}
