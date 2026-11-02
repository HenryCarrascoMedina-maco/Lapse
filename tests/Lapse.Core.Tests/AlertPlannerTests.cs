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
        Assert.Empty(AlertPlanner.Plan(Reconciled(daysRemaining: 45, ScanOutcome.Unchanged), Plan, Now));

    [Fact]
    public void Warning_threshold_goes_to_the_owner_only()
    {
        var alert = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: 20, ScanOutcome.Unchanged), Plan, Now));

        Assert.Equal(AlertKind.Threshold, alert.Kind);
        Assert.Equal(30, alert.ThresholdDays);
        Assert.Equal(["ana"], alert.Recipients.Select(owner => owner.Name));
    }

    [Fact]
    public void Critical_threshold_also_goes_to_the_backup()
    {
        var alert = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: 5, ScanOutcome.Unchanged), Plan, Now));

        Assert.Equal(7, alert.ThresholdDays);
        Assert.Equal(["ana", "it"], alert.Recipients.Select(owner => owner.Name));
    }

    [Fact]
    public void Expired_item_raises_an_expired_alert_with_backup()
    {
        var alert = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: -2, ScanOutcome.Unchanged), Plan, Now));

        Assert.Equal(AlertKind.Expired, alert.Kind);
        Assert.Contains(alert.Recipients, owner => owner.Name == "it");
    }

    [Fact]
    public void Renewal_is_announced_when_an_earlier_date_was_warned_about()
    {
        var alert = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: 90, ScanOutcome.Unchanged), Plan, Now, renewedFrom: Now.AddDays(-1)));

        Assert.Equal(AlertKind.Renewed, alert.Kind);
        Assert.Equal(Now.AddDays(-1), alert.PreviousExpiresAt);
        Assert.Equal(["ana"], alert.Recipients.Select(owner => owner.Name));
    }

    [Fact]
    public void Renewal_nobody_was_warned_about_is_not_announced() =>
        Assert.Empty(AlertPlanner.Plan(Reconciled(daysRemaining: 90, ScanOutcome.Renewed), Plan, Now));

    [Fact]
    public void Failed_scan_keeps_counting_down_with_the_last_observed_date()
    {
        var alert = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: 3, ScanOutcome.Failed, failingSince: Now.AddHours(-2)), Plan, Now));

        Assert.Equal(AlertKind.Threshold, alert.Kind);
        Assert.Equal(7, alert.ThresholdDays);
    }

    [Fact]
    public void Failed_scan_does_not_announce_a_renewal() =>
        Assert.Empty(AlertPlanner.Plan(Reconciled(daysRemaining: 90, ScanOutcome.Failed, failingSince: Now), Plan, Now, renewedFrom: Now.AddDays(-1)));

    [Fact]
    public void Item_failing_for_less_than_a_day_raises_nothing() =>
        Assert.Empty(AlertPlanner.Plan(Reconciled(daysRemaining: null, ScanOutcome.Failed, failingSince: Now.AddHours(-22)), Plan, Now));

    [Fact]
    public void Item_failing_for_a_day_raises_an_unverified_alert()
    {
        var failingSince = Now.AddDays(-1).AddSeconds(5);
        var alert = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: null, ScanOutcome.Failed, failingSince), Plan, Now));

        Assert.Equal(AlertKind.Unverified, alert.Kind);
        Assert.Equal(failingSince, alert.RecordFor("email").ExpiresAt);
        Assert.Contains("could not check it since", AlertMessage.For(new AlertDigest(alert.Recipients, [alert])).Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Certificate_problem_raises_its_own_alert()
    {
        var reconciliation = Reconciled(daysRemaining: 60, ScanOutcome.Unchanged);
        reconciliation = reconciliation with { Item = reconciliation.Item with { Problem = "the certificate is not valid for the name api.example.com" } };

        var alert = Assert.Single(AlertPlanner.Plan(reconciliation, Plan, Now));

        Assert.Equal(AlertKind.Problem, alert.Kind);
        Assert.Contains("not valid for the name", AlertMessage.For(new AlertDigest(alert.Recipients, [alert])).Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_subject_names_the_item_and_the_days_left()
    {
        var alert = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: 5, ScanOutcome.Unchanged), Plan, Now));

        Assert.Equal("[Lapse] api.example.com:443 expires in 5 days", AlertMessage.For(new AlertDigest(alert.Recipients, [alert])).Subject);
    }

    [Fact]
    public void Digest_puts_the_most_urgent_alert_in_the_subject()
    {
        var soon = Assert.Single(AlertPlanner.Plan(Reconciled(daysRemaining: 20, ScanOutcome.Unchanged), Plan, Now));
        var sooner = soon with { Item = soon.Item with { Name = "mail.example.com:465" }, DaysRemaining = 2 };

        var message = AlertMessage.For(new AlertDigest(soon.Recipients, [soon, sooner]));

        Assert.Equal("[Lapse] mail.example.com:465 expires in 2 days, and 1 more", message.Subject);
        Assert.StartsWith("2 alerts:", message.Body, StringComparison.Ordinal);
    }

    private static Reconciliation Reconciled(int? daysRemaining, ScanOutcome outcome, DateTimeOffset? failingSince = null)
    {
        var expiresAt = daysRemaining is { } days ? Now.AddDays(days).AddHours(1) : (DateTimeOffset?)null;
        var status = outcome == ScanOutcome.Failed ? ItemStatus.Failing : ItemStatus.Active;
        var error = outcome == ScanOutcome.Failed ? "no response within 10 s" : null;
        var item = new Item(Target.Key, Target.Key, Target.Key.Value, "ana", expiresAt, status, Now, error, failingSince);
        return new Reconciliation(item, outcome, Now.AddDays(-10), Observation: null);
    }
}
