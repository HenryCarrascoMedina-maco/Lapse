using Lapse.Core.Items;
using Lapse.Core.Scanning;

namespace Lapse.Core.Tests;

public class ReconcilerTests
{
    private static readonly DateTimeOffset Now = Plans.Today;
    private static readonly WatchTarget Target = Plans.Tls("api.example.com:443");

    [Fact]
    public void First_observation_creates_a_new_item()
    {
        var result = Reconciler.Reconcile(null, Target, Observed(Now.AddDays(30)), Now);

        Assert.Equal(ScanOutcome.New, result.Outcome);
        Assert.Equal(ItemStatus.Active, result.Item.Status);
        Assert.Equal(Now.AddDays(30), result.Item.ExpiresAt);
    }

    [Fact]
    public void Later_expiry_is_a_verified_renewal()
    {
        var existing = Existing(Now.AddDays(5));

        var result = Reconciler.Reconcile(existing, Target, Observed(Now.AddDays(90)), Now);

        Assert.Equal(ScanOutcome.Renewed, result.Outcome);
        Assert.Equal(ItemStatus.Renewed, result.Item.Status);
        Assert.Equal(Now.AddDays(5), result.PreviousExpiresAt);
    }

    [Fact]
    public void Earlier_expiry_is_reported_as_a_change()
    {
        var result = Reconciler.Reconcile(Existing(Now.AddDays(90)), Target, Observed(Now.AddDays(10)), Now);

        Assert.Equal(ScanOutcome.Changed, result.Outcome);
    }

    [Fact]
    public void Same_expiry_is_unchanged()
    {
        var result = Reconciler.Reconcile(Existing(Now.AddDays(10)), Target, Observed(Now.AddDays(10)), Now);

        Assert.Equal(ScanOutcome.Unchanged, result.Outcome);
    }

    [Fact]
    public void Failure_keeps_the_last_known_expiry()
    {
        var result = Reconciler.Reconcile(Existing(Now.AddDays(10)), Target, Result.Failure<Observation>("no response"), Now);

        Assert.Equal(ScanOutcome.Failed, result.Outcome);
        Assert.Equal(ItemStatus.Failing, result.Item.Status);
        Assert.Equal(Now.AddDays(10), result.Item.ExpiresAt);
        Assert.Equal("no response", result.Item.LastError);
        Assert.Null(result.Observation);
    }

    [Fact]
    public void First_success_after_failures_counts_as_new()
    {
        var failing = Existing(expiresAt: null) with { Status = ItemStatus.Failing };

        var result = Reconciler.Reconcile(failing, Target, Observed(Now.AddDays(10)), Now);

        Assert.Equal(ScanOutcome.New, result.Outcome);
    }

    private static Result<Observation> Observed(DateTimeOffset expiresAt) =>
        Result.Success(new Observation(Target.Key.Value, expiresAt, "sha256"));

    private static Item Existing(DateTimeOffset? expiresAt) =>
        new(Target.Key, Target.Key.Value, "ana", expiresAt, ItemStatus.Active, Now.AddDays(-1), LastError: null);
}
