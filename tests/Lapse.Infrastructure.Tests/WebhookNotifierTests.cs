using System.Net;
using System.Text.Json;
using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Infrastructure.Configuration;
using Lapse.Infrastructure.Notifications;

namespace Lapse.Infrastructure.Tests;

public class WebhookNotifierTests
{
    private const string Url = "https://hooks.example.com/services/T0K3N";

    [Fact]
    public async Task Posts_a_json_payload_with_the_alert()
    {
        var handler = StubHttpHandler.Responding(HttpStatusCode.OK);

        var result = await Notifier(handler).SendAsync(Alert(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var (uri, body) = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(Url), uri);
        using var payload = JsonDocument.Parse(body!);
        var root = payload.RootElement;
        Assert.Equal("threshold", root.GetProperty("event").GetString());
        Assert.Equal("[Lapse] api.example.com:443 expires in 5 days", root.GetProperty("text").GetString());
        Assert.Equal("api.example.com:443", root.GetProperty("item").GetProperty("key").GetString());
        Assert.Equal(5, root.GetProperty("item").GetProperty("daysRemaining").GetInt32());
    }

    [Fact]
    public async Task Error_status_is_reported_without_the_url()
    {
        var result = await Notifier(StubHttpHandler.Responding(HttpStatusCode.InternalServerError)).SendAsync(Alert(), CancellationToken.None);

        Assert.Equal("the webhook answered HTTP 500", result.Error);
    }

    [Fact]
    public async Task Connection_failure_is_reported_without_the_url()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException($"Could not connect to {Url}"));

        var result = await Notifier(handler).SendAsync(Alert(), CancellationToken.None);

        Assert.Equal("could not connect to the webhook", result.Error);
        Assert.DoesNotContain("T0K3N", result.Error, StringComparison.Ordinal);
    }

    private static WebhookNotifier Notifier(StubHttpHandler handler) =>
        new(new HttpClient(handler), new Secret(Url), new FixedTime(Dates.Now));

    private static PlannedAlert Alert()
    {
        var expiresAt = Dates.Now.AddDays(5).AddHours(1);
        var item = new Item(new ItemKey(ItemKind.Tls, "api.example.com:443"), "api.example.com:443", "it", expiresAt, ItemStatus.Active, Dates.Now, null);
        return new PlannedAlert(item, AlertKind.Threshold, 7, 5, expiresAt, [new Owner("it", "it@example.com", null)], null);
    }
}
