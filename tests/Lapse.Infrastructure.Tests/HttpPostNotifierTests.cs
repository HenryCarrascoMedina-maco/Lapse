using System.Net;
using System.Text.Json;
using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Infrastructure.Configuration;
using Lapse.Infrastructure.Notifications;

namespace Lapse.Infrastructure.Tests;

public class HttpPostNotifierTests
{
    private const string Url = "https://hooks.example.com/services/T0K3N";
    private const string BotToken = "123456:bot-token-secret";

    [Fact]
    public async Task Webhook_posts_a_json_payload_with_the_alert()
    {
        var handler = StubHttpHandler.Responding(HttpStatusCode.OK);

        var result = await Webhook(handler).SendAsync(Alert(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(Url), request.Uri);
        using var payload = JsonDocument.Parse(request.Body!);
        var root = payload.RootElement;
        Assert.Equal("threshold", root.GetProperty("event").GetString());
        Assert.Equal("[Lapse] api.example.com:443 expires in 5 days", root.GetProperty("text").GetString());
        Assert.Equal("api.example.com:443", root.GetProperty("item").GetProperty("key").GetString());
        Assert.Equal(5, root.GetProperty("item").GetProperty("daysRemaining").GetInt32());
    }

    [Fact]
    public async Task Error_status_is_reported_without_the_url()
    {
        var result = await Webhook(StubHttpHandler.Responding(HttpStatusCode.InternalServerError)).SendAsync(Alert(), CancellationToken.None);

        Assert.Equal("the webhook answered HTTP 500", result.Error);
    }

    [Fact]
    public async Task Connection_failure_is_reported_without_the_url()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException($"Could not connect to {Url}"));

        var result = await Webhook(handler).SendAsync(Alert(), CancellationToken.None);

        Assert.Equal("could not connect to the webhook", result.Error);
        Assert.DoesNotContain("T0K3N", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teams_receives_an_adaptive_card()
    {
        var handler = StubHttpHandler.Responding(HttpStatusCode.Accepted);

        var result = await HttpPostNotifier.Teams(new HttpClient(handler), new Secret(Url)).SendAsync(Alert(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        using var payload = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        var card = payload.RootElement.GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive", card.GetProperty("contentType").GetString());
        Assert.Equal("[Lapse] api.example.com:443 expires in 5 days", card.GetProperty("content").GetProperty("body")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Telegram_sends_the_message_to_the_chat()
    {
        var handler = StubHttpHandler.Responding(HttpStatusCode.OK);

        var result = await Telegram(handler).SendAsync(Alert(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"https://api.telegram.org/bot{BotToken}/sendMessage", request.Uri.ToString());
        using var payload = JsonDocument.Parse(request.Body!);
        Assert.Equal("-1001234", payload.RootElement.GetProperty("chat_id").GetString());
        Assert.StartsWith("api.example.com:443 expires in 5 days", payload.RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Telegram_errors_never_include_the_bot_token()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException($"Could not connect to https://api.telegram.org/bot{BotToken}/sendMessage"));

        var result = await Telegram(handler).SendAsync(Alert(), CancellationToken.None);

        Assert.Equal("could not connect to Telegram", result.Error);
        Assert.DoesNotContain(BotToken, result.Error, StringComparison.Ordinal);
    }

    private static HttpPostNotifier Webhook(StubHttpHandler handler) =>
        HttpPostNotifier.Webhook(new HttpClient(handler), new Secret(Url), new FixedTime(Dates.Now));

    private static HttpPostNotifier Telegram(StubHttpHandler handler) =>
        HttpPostNotifier.Telegram(new HttpClient(handler), new Secret(BotToken), "-1001234");

    private static PlannedAlert Alert()
    {
        var expiresAt = Dates.Now.AddDays(5).AddHours(1);
        var key = new ItemKey(ItemKind.Tls, "api.example.com:443");
        var item = new Item(key, key, "api.example.com:443", "it", expiresAt, ItemStatus.Active, Dates.Now, null);
        return new PlannedAlert(item, AlertKind.Threshold, 7, 5, expiresAt, [new Owner("it", "it@example.com", null)], null);
    }
}
