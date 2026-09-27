using Lapse.Core.Alerts;
using Lapse.Core.Items;

namespace Lapse.Core.Tests;

public class AlertPolicyTests
{
    private static readonly DateTimeOffset Now = Plans.Today;
    private static readonly AlertPolicy Policy = AlertPolicy.Default;

    [Theory]
    [InlineData(45, null)]
    [InlineData(30, 30)]
    [InlineData(20, 30)]
    [InlineData(10, 14)]
    [InlineData(5, 7)]
    [InlineData(0, 1)]
    public void Tightest_threshold_is_the_smallest_one_already_reached(int daysRemaining, int? expected) =>
        Assert.Equal(expected, Policy.TightestThresholdReached(daysRemaining));

    [Theory]
    [InlineData(-1, Urgency.Expired)]
    [InlineData(3, Urgency.Critical)]
    [InlineData(7, Urgency.Critical)]
    [InlineData(20, Urgency.Warning)]
    [InlineData(31, Urgency.Ok)]
    public void Urgency_follows_the_days_remaining(int days, Urgency expected) =>
        Assert.Equal(expected, Policy.UrgencyOf(Now.AddDays(days).AddHours(1), Now));

    [Fact]
    public void Unknown_expiry_has_unknown_urgency() =>
        Assert.Equal(Urgency.Unknown, Policy.UrgencyOf(null, Now));

    [Fact]
    public void Days_remaining_rounds_toward_zero() =>
        Assert.Equal(5, AlertPolicy.DaysRemaining(Now.AddDays(5.9), Now));

    [Fact]
    public void Failing_item_is_reported_as_error_even_if_it_expires_soon()
    {
        var item = new Item(new ItemKey(ItemKind.Tls, "a:443"), new ItemKey(ItemKind.Tls, "a:443"), "a", "ana", Now.AddDays(1), ItemStatus.Failing, Now, "no response");

        Assert.Equal(ItemHealth.Error, Policy.HealthOf(item, Now));
    }

    [Fact]
    public void Renewed_item_that_still_expires_soon_is_reported_by_urgency()
    {
        var item = new Item(new ItemKey(ItemKind.Tls, "a:443"), new ItemKey(ItemKind.Tls, "a:443"), "a", "ana", Now.AddDays(3), ItemStatus.Renewed, Now, null);

        Assert.Equal(ItemHealth.Critical, Policy.HealthOf(item, Now));
    }

    [Fact]
    public void Thresholds_are_normalized_to_descending_and_distinct()
    {
        var policy = new AlertPolicy([7, 30, 7, 1]);

        Assert.Equal([30, 7, 1], policy.WarnAtDays);
    }

    [Fact]
    public void Non_positive_thresholds_are_rejected() =>
        Assert.Throws<ArgumentException>(() => new AlertPolicy([30, 0]));
}
