using Lapse.Core.Items;

namespace Lapse.Core.Alerts;

public enum AlertKind
{
    Threshold,
    Expired,
    Renewed,
}

public sealed record PlannedAlert(
    Item Item,
    AlertKind Kind,
    int ThresholdDays,
    int DaysRemaining,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Owner> Recipients,
    DateTimeOffset? PreviousExpiresAt)
{
    public AlertRecord RecordFor(string channel) => new(Item.Key, Kind, ThresholdDays, ExpiresAt, channel);
}

public sealed record AlertRecord(ItemKey Key, AlertKind Kind, int ThresholdDays, DateTimeOffset ExpiresAt, string Channel);
