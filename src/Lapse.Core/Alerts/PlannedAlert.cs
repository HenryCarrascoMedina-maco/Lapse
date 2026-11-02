using Lapse.Core.Items;

namespace Lapse.Core.Alerts;

public enum AlertKind
{
    Threshold,
    Expired,
    Renewed,
    Unverified,
    Problem,
}

public sealed record PlannedAlert(
    Item Item,
    AlertKind Kind,
    int ThresholdDays,
    int? DaysRemaining,
    IReadOnlyList<Owner> Recipients,
    DateTimeOffset? PreviousExpiresAt = null)
{
    public DateTimeOffset? ExpiresAt => Item.ExpiresAt;

    // An unverified alert is sent once per failure episode; every other alert once per expiry date.
    public AlertRecord RecordFor(string channel) =>
        new(Item.Key, Kind, ThresholdDays, (Kind == AlertKind.Unverified ? Item.FailingSince : Item.ExpiresAt)!.Value, channel);
}

public sealed record AlertRecord(ItemKey Key, AlertKind Kind, int ThresholdDays, DateTimeOffset ExpiresAt, string Channel);

public sealed record AlertDigest(IReadOnlyList<Owner> Recipients, IReadOnlyList<PlannedAlert> Alerts)
{
    public bool IsTest => Alerts.Count == 0;

    public static AlertDigest Test(IReadOnlyList<Owner> recipients) => new(recipients, []);
}
