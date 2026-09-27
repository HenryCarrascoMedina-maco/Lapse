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
        var result = Single([], Observed((Target.Key.Value, Now.AddDays(30))));

        Assert.Equal(ScanOutcome.New, result.Outcome);
        Assert.Equal(ItemStatus.Active, result.Item.Status);
        Assert.Equal(Target.Key, result.Item.Target);
        Assert.Equal(Now.AddDays(30), result.Item.ExpiresAt);
    }

    [Fact]
    public void Later_expiry_is_a_verified_renewal()
    {
        var result = Single([Existing(Target.Key.Value, Now.AddDays(5))], Observed((Target.Key.Value, Now.AddDays(90))));

        Assert.Equal(ScanOutcome.Renewed, result.Outcome);
        Assert.Equal(ItemStatus.Renewed, result.Item.Status);
        Assert.Equal(Now.AddDays(5), result.PreviousExpiresAt);
    }

    [Fact]
    public void Earlier_expiry_is_reported_as_a_change() =>
        Assert.Equal(ScanOutcome.Changed, Single([Existing(Target.Key.Value, Now.AddDays(90))], Observed((Target.Key.Value, Now.AddDays(10)))).Outcome);

    [Fact]
    public void Same_expiry_is_unchanged() =>
        Assert.Equal(ScanOutcome.Unchanged, Single([Existing(Target.Key.Value, Now.AddDays(10))], Observed((Target.Key.Value, Now.AddDays(10)))).Outcome);

    [Fact]
    public void Failure_keeps_the_last_known_expiry()
    {
        var result = Single([Existing(Target.Key.Value, Now.AddDays(10))], Result.Failure<IReadOnlyList<Observation>>("no response"));

        Assert.Equal(ScanOutcome.Failed, result.Outcome);
        Assert.Equal(ItemStatus.Failing, result.Item.Status);
        Assert.Equal(Now.AddDays(10), result.Item.ExpiresAt);
        Assert.Equal("no response", result.Item.LastError);
        Assert.Null(result.Observation);
    }

    [Fact]
    public void First_failure_is_reported_on_the_target_itself()
    {
        var result = Single([], Result.Failure<IReadOnlyList<Observation>>("no response"));

        Assert.Equal(Target.Key, result.Item.Key);
        Assert.Null(result.Item.ExpiresAt);
    }

    [Fact]
    public void First_success_after_failures_counts_as_new()
    {
        var failing = Existing(Target.Key.Value, expiresAt: null) with { Status = ItemStatus.Failing };

        Assert.Equal(ScanOutcome.New, Single([failing], Observed((Target.Key.Value, Now.AddDays(10)))).Outcome);
    }

    [Fact]
    public void One_target_can_produce_several_items()
    {
        var results = Reconciler.Reconcile(
            [Existing("secret-a", Now.AddDays(5)), Existing("secret-gone", Now.AddDays(9))],
            Target,
            Observed(("secret-a", Now.AddDays(5)), ("secret-b", Now.AddDays(60))),
            Now);

        Assert.Equal(["secret-a", "secret-b"], results.Select(result => result.Item.Key.Value));
        Assert.Equal([ScanOutcome.Unchanged, ScanOutcome.New], results.Select(result => result.Outcome));
    }

    [Fact]
    public void Failure_keeps_every_item_of_the_target()
    {
        var results = Reconciler.Reconcile(
            [Existing("secret-a", Now.AddDays(5)), Existing("secret-b", Now.AddDays(60))],
            Target,
            Result.Failure<IReadOnlyList<Observation>>("token rejected"),
            Now);

        Assert.Equal(["secret-a", "secret-b"], results.Select(result => result.Item.Key.Value));
        Assert.All(results, result => Assert.Equal(ScanOutcome.Failed, result.Outcome));
    }

    private static Reconciliation Single(IReadOnlyCollection<Item> existing, Result<IReadOnlyList<Observation>> result) =>
        Assert.Single(Reconciler.Reconcile(existing, Target, result, Now));

    private static Result<IReadOnlyList<Observation>> Observed(params (string Key, DateTimeOffset ExpiresAt)[] items) =>
        Result.Success<IReadOnlyList<Observation>>(
            [.. items.Select(item => new Observation(new ItemKey(ItemKind.Tls, item.Key), item.Key, item.ExpiresAt, "sha256"))]);

    private static Item Existing(string key, DateTimeOffset? expiresAt) =>
        new(new ItemKey(ItemKind.Tls, key), Target.Key, key, "ana", expiresAt, ItemStatus.Active, Now.AddDays(-1), LastError: null);
}
