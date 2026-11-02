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
        Assert.Equal(2, email.Digests.Count);
        Assert.Equal(0, afterRenewal.Delivery.Sent);
    }

    [Fact]
    public async Task Renewal_seen_by_a_scan_without_alerts_is_still_announced()
    {
        source.Expires(Host, time.Now.AddDays(5));
        await RunAsync();

        source.Expires(Host, time.Now.AddDays(90));
        await RunAsync(notify: false);
        await RunAsync();

        Assert.Equal([AlertKind.Threshold, AlertKind.Renewed], email.Sent.Select(alert => alert.Kind));
    }

    [Fact]
    public async Task Renewal_is_retried_on_a_channel_that_failed()
    {
        source.Expires(Host, time.Now.AddDays(5));
        await RunAsync();

        source.Expires(Host, time.Now.AddDays(40));
        webhook.FailWith = "down";
        await RunAsync();
        webhook.FailWith = null;
        time.Now = time.Now.AddDays(15);
        await RunAsync();

        Assert.Equal([AlertKind.Threshold, AlertKind.Renewed, AlertKind.Threshold], webhook.Sent.Select(alert => alert.Kind));
        Assert.Single(email.Sent, alert => alert.Kind == AlertKind.Renewed);
    }

    [Fact]
    public async Task Long_digests_are_split_to_fit_the_channel()
    {
        var hosts = Enumerable.Range(0, 5).Select(index => $"host-{index}.example.com:443").ToList();
        hosts.ForEach(host => source.Expires(host, time.Now.AddDays(5)));
        var chat = new RecordingNotifier("telegram") { MaxAlertsPerMessage = 2 };

        await new ScanPipeline([source], [chat], store, time).RunAsync(Plans.For([.. hosts.Select(host => Plans.Tls(host))]), notify: true, CancellationToken.None);

        Assert.Equal([2, 2, 1], chat.Digests.Select(digest => digest.Alerts.Count));
    }

    [Fact]
    public async Task Unreachable_item_keeps_alerting_with_its_last_observed_date()
    {
        source.Expires(Host, time.Now.AddDays(20));
        await RunAsync();

        source.Fails(Host, "no response");
        time.Now = time.Now.AddDays(15);
        await RunAsync();

        Assert.Equal([30, 7], email.Sent.Select(alert => alert.ThresholdDays));
    }

    [Fact]
    public async Task Item_that_stays_unreachable_raises_one_unverified_alert()
    {
        source.Fails(Host, "no response");
        var first = await RunAsync();

        time.Now = time.Now.AddDays(1);
        await RunAsync();
        time.Now = time.Now.AddDays(1);
        await RunAsync();

        Assert.Equal(0, first.Delivery.Sent);
        Assert.Equal([AlertKind.Unverified], email.Sent.Select(alert => alert.Kind));
    }

    [Fact]
    public async Task Alerts_of_one_scan_are_grouped_into_one_message_per_channel()
    {
        var other = "mail.example.com:465";
        source.Expires(Host, time.Now.AddDays(20));
        source.Expires(other, time.Now.AddDays(10));
        var perRecipient = new RecordingNotifier("email", perRecipient: true);
        var groupPlan = Plans.For(Plans.Tls(Host), Plans.Tls(other), Plans.Tls("vpn.example.com:443", owner: "it"));
        source.Expires("vpn.example.com:443", time.Now.AddDays(25));

        var report = await new ScanPipeline([source], [perRecipient, webhook], store, time).RunAsync(groupPlan, notify: true, CancellationToken.None);

        Assert.Equal(6, report.Delivery.Sent);
        Assert.Equal([["ana", "ana"], ["it"]], perRecipient.Digests.Select(digest => digest.Alerts.Select(alert => alert.Item.Owner)));
        Assert.Equal(3, Assert.Single(webhook.Digests).Alerts.Count);
    }

    [Fact]
    public async Task Failed_delivery_is_retried_on_the_next_scan()
    {
        source.Expires(Host, time.Now.AddDays(5));
        email.FailWith = "SMTP is down";

        var failed = await RunAsync();
        email.FailWith = null;
        await RunAsync();

        Assert.Equal([new ItemKey(ItemKind.Tls, Host)], Assert.Single(failed.Delivery.Failures).Keys);
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
    public async Task Source_that_throws_only_fails_its_own_target()
    {
        var other = "mail.example.com:465";
        source.Expires(other, time.Now.AddDays(5));
        var twoTargets = Plans.For(Plans.Tls(Host), Plans.Tls(other));

        var report = await Pipeline().RunAsync(twoTargets, notify: true, CancellationToken.None);

        Assert.Equal([ScanOutcome.Failed, ScanOutcome.New], report.Entries.Select(entry => entry.Outcome));
        Assert.StartsWith("unexpected error:", report.Entries[0].Item.LastError, StringComparison.Ordinal);
        Assert.Equal(2, report.Delivery.Sent);
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

    [Fact]
    public async Task Failing_target_keeps_every_item_it_produced()
    {
        source.Yields(Host, ("secret-a", time.Now.AddDays(40)), ("secret-b", time.Now.AddDays(50)));
        await RunAsync();

        source.Fails(Host, "token rejected");
        var report = await RunAsync();

        Assert.Equal(2, store.Items.Count);
        Assert.All(report.Entries, entry => Assert.Equal(ScanOutcome.Failed, entry.Outcome));
    }

    [Fact]
    public async Task Items_that_disappear_from_a_target_are_forgotten()
    {
        source.Yields(Host, ("secret-a", time.Now.AddDays(40)), ("secret-b", time.Now.AddDays(50)));
        await RunAsync();

        source.Yields(Host, ("secret-a", time.Now.AddDays(40)));
        await RunAsync();

        Assert.Equal(["secret-a"], store.Items.Keys.Select(key => key.Value));
    }

    [Fact]
    public async Task Items_are_assigned_to_the_owner_whose_email_matches_a_contact()
    {
        source.Results[Host] = Result.Success<IReadOnlyList<Observation>>(
        [
            new Observation(new ItemKey(ItemKind.Tls, "secret-a"), "secret-a", time.Now.AddDays(40), null, ["IT@example.com"]),
            new Observation(new ItemKey(ItemKind.Tls, "secret-b"), "secret-b", time.Now.AddDays(40), null, ["stranger@example.com"]),
        ]);

        var report = await RunAsync();

        Assert.Equal(["it", "ana"], report.Entries.Select(entry => entry.Item.Owner));
    }

    [Fact]
    public async Task Alert_that_no_channel_can_deliver_is_reported()
    {
        source.Expires(Host, time.Now.AddDays(5));
        var emailOnly = new RecordingNotifier("email", perRecipient: true) { RequiresEmail = true };

        var report = await new ScanPipeline([source], [emailOnly], store, time)
            .RunAsync(Plans.For(Plans.Tls(Host, owner: "finance")), notify: true, CancellationToken.None);

        Assert.Equal([new ItemKey(ItemKind.Tls, Host)], report.Delivery.Unreachable);
        Assert.Empty(emailOnly.Sent);
    }

    private ScanPipeline Pipeline() => new([source], [email, webhook], store, time);

    private Task<ScanReport> RunAsync(bool notify = true) => Pipeline().RunAsync(plan, notify, CancellationToken.None);
}
