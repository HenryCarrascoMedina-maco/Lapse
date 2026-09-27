using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Core.Scanning;

namespace Lapse.Core.Tests;

public class AlertPlannerTests
{
    private static readonly DateTimeOffset Now = Plans.Today;
    private static readonly WatchTarget Target = Plans.Tls("api.example.com:443");
    private static readonly WatchPlan Plan = Plans.For(Target);

    [Fact]
    public void No_alert_before_the_first_threshold() =>
        Assert.Null(AlertPlanner.Plan(Reconciled(daysRemaining: 45, ScanOutcome.Unchanged), Plan, Now));

    [Fact]
    public void Warning_threshold_goes_to_the_owner_only()
    {
        var alert = AlertPlanner.Plan(Reconciled(daysRemaining: 20, ScanOutcome.Unchanged), Plan, Now)!;

        Assert.Equal(AlertKind.Threshold, alert.Kind);
        Assert.Equal(30, alert.ThresholdDays);
        Assert.Equal(["ana"], alert.Recipients.Select(owner => owner.Name));
    }

    [Fact]
    public void Critical_threshold_also_goes_to_the_backup()
    {
        var alert = AlertPlanner.Plan(Reconciled(daysRemaining: 5, ScanOutcome.Unchanged), Plan, Now)!;

        Assert.Equal(7, alert.ThresholdDays);
        Assert.Equal(["ana", "it"], alert.Recipients.Select(owner => owner.Name));
    }

    [Fact]
    public void Expired_item_raises_an_expired_alert_with_backup()
    {
        var alert = AlertPlanner.Plan(Reconciled(daysRemaining: -2, ScanOutcome.Unchanged), Plan, Now)!;

        Assert.Equal(AlertKind.Expired, alert.Kind);
        Assert.Contains(alert.Recipients, owner => owner.Name == "it");
    }

    [Fact]
    public void Renewal_is_announced_to_the_owner()
    {
        var alert = AlertPlanner.Plan(Reconciled(daysRemaining: 90, ScanOutcome.Renewed), Plan, Now)!;

        Assert.Equal(AlertKind.Renewed, alert.Kind);
        Assert.Equal(["ana"], alert.Recipients.Select(owner => owner.Name));
    }

    [Fact]
    public void Failed_scans_do_not_raise_alerts() =>
        Assert.Null(AlertPlanner.Plan(Reconciled(daysRemaining: 1, ScanOutcome.Failed), Plan, Now));

    [Fact]
    public void Message_subject_names_the_item_and_the_days_left()
    {
        var alert = AlertPlanner.Plan(Reconciled(daysRemaining: 5, ScanOutcome.Unchanged), Plan, Now)!;

        Assert.Equal("[Lapse] api.example.com:443 expires in 5 days", AlertMessage.For(alert).Subject);
    }

    private static Reconciliation Reconciled(int daysRemaining, ScanOutcome outcome)
    {
        var expiresAt = Now.AddDays(daysRemaining).AddHours(1);
        var item = new Item(Target.Key, Target.Key.Value, "ana", expiresAt, ItemStatus.Active, Now, null);
        return new Reconciliation(item, outcome, Now.AddDays(-10), Observation: null);
    }
}
