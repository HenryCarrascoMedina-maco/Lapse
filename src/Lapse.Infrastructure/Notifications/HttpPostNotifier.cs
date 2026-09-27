using System.Globalization;
using System.Net.Http.Headers;
using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Infrastructure.Configuration;

namespace Lapse.Infrastructure.Notifications;

public sealed class HttpPostNotifier : INotifier
{
    private readonly HttpClient http;
    private readonly Secret url;
    private readonly string displayName;
    private readonly Func<PlannedAlert, byte[]> payload;

    private HttpPostNotifier(string channel, string displayName, HttpClient http, Secret url, Func<PlannedAlert, byte[]> payload)
    {
        Channel = channel;
        this.displayName = displayName;
        this.http = http;
        this.url = url;
        this.payload = payload;
    }

    public string Channel { get; }

    public static HttpPostNotifier Webhook(HttpClient http, Secret url, TimeProvider time) =>
        new("webhook", "the webhook", http, url, alert => AlertPayloads.Webhook(alert, time.GetUtcNow()));

    public static HttpPostNotifier Teams(HttpClient http, Secret url) =>
        new("teams", "Microsoft Teams", http, url, AlertPayloads.Teams);

    public static HttpPostNotifier Telegram(HttpClient http, Secret botToken, string chatId) =>
        new("telegram", "Telegram", http, new Secret($"https://api.telegram.org/bot{botToken.Reveal()}/sendMessage"), alert => AlertPayloads.Telegram(alert, chatId));

    public bool CanDeliver(PlannedAlert alert) => true;

    public async Task<Result<Unit>> SendAsync(PlannedAlert alert, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(payload(alert));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            using var response = await http.PostAsync(new Uri(url.Reveal()), content, cancellationToken);
            return response.IsSuccessStatusCode
                ? Result.Success(Unit.Value)
                : Result.Failure<Unit>(string.Create(CultureInfo.InvariantCulture, $"{displayName} answered HTTP {(int)response.StatusCode}"));
        }
        catch (HttpRequestException)
        {
            return Result.Failure<Unit>($"could not connect to {displayName}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<Unit>($"{displayName} did not answer in time");
        }
    }
}
