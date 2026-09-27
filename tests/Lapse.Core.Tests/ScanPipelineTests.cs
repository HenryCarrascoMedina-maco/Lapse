using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Core.Scanning;

namespace Lapse.Core.Tests;

public class ScanPipelineTests
{
    private const string Host = "api.example.com:443";

    private readonly FakeTime time = new(Plans.Today);
    private readonly FakeSource source = new(ItemKind.Tls);
    private readonly RecordingNotifier email = new("email");
    private readonly RecordingNotifier webhook = new("webhook");
    private readonly InMemoryItemStore store = new();
    private readonly WatchPlan plan = Plans.For(Plans.Tls(Host));

    [Fact]
    public async Task Alert_is_sent_once_per_channel()
    {
        source.Expires(Host, time.Now.AddDays(5));

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal(2, first.Delivery.Sent);
        Assert.Equal(0, second.Delivery.Sent);
        Assert.Equal(2, second.Delivery.Skipped);
        Assert.Single(email.Sent);
        Assert.Single(webhook.Sent);
    }

    [Fact]
    public async Task Crossing_a_tighter_threshold_sends_a_new_alert()
    {
        source.Expires(Host, time.Now.AddDays(10));
        await RunAsync();

        time.Now = time.Now.AddDays(5);
        await RunAsync();

        Assert.Equal([14, 7], email.Sent.Select(alert => alert.ThresholdDays));
    }

    [Fact]
    public async Task Renewal_is_announced_and_thresholds_start_over()
    {
        source.Expires(Host, time.Now.AddDays(5));
        await RunAsync();

        source.Expires(Host, time.Now.AddDays(20));
        var renewal = await RunAsync();
        var afterRenewal = await RunAsync();

        Assert.Equal(ScanOutcome.Renewed, renewal.Entries[0].Outcome);
        Assert.Equal([AlertKind.Threshold, AlertKind.Renewed, AlertKind.Threshold], email.Sent.Select(alert => alert.Kind));
        Assert.Equal(30, email.Sent[^1].ThresholdDays);
        Assert.Equal(2, afterRenewal.Delivery.Sent);
    }

    [Fact]
    public async Task Failed_delivery_is_retried_on_the_next_scan()
    {
        source.Expires(Host, time.Now.AddDays(5));
        email.FailWith = "SMTP is down";

        var failed = await RunAsync();
        email.FailWith = null;
        await RunAsync();

        Assert.Single(failed.Delivery.Failures);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task Disabled_notifications_send_nothing()
    {
        source.Expires(Host, time.Now.AddDays(1));

        var report = await RunAsync(notify: false);

        Assert.Equal(DeliverySummary.None, report.Delivery);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Source_failure_is_reported_without_alerts()
    {
        source.Fails(Host, "no response");

        var report = await RunAsync();

        Assert.Equal(ScanOutcome.Failed, report.Entries[0].Outcome);
        Assert.Equal(0, report.Delivery.Sent);
    }

    [Fact]
    public async Task Target_without_source_fails_instead_of_throwing()
    {
        var domainPlan = Plans.For(new WatchTarget(new ItemKey(ItemKind.Domain, "example.com"), "ana"));

        var report = await Pipeline().RunAsync(domainPlan, notify: true, CancellationToken.None);

        Assert.Equal(ScanOutcome.Failed, report.Entries[0].Outcome);
    }

    [Fact]
    public async Task Items_removed_from_the_plan_are_forgotten()
    {
        source.Expires(Host, time.Now.AddDays(40));
        await RunAsync();

        await Pipeline().RunAsync(Plans.For(), notify: true, CancellationToken.None);

        Assert.Empty(store.Items);
    }

    private ScanPipeline Pipeline() => new([source], [email, webhook], store, time);

    private Task<ScanReport> RunAsync(bool notify = true) => Pipeline().RunAsync(plan, notify, CancellationToken.None);
}
