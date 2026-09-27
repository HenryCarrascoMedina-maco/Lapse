using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Infrastructure.Configuration;
using Lapse.Infrastructure.Serialization;

namespace Lapse.Infrastructure.Notifications;

public sealed class WebhookNotifier(HttpClient http, Secret url, TimeProvider time) : INotifier
{
    public string Channel => "webhook";

    public bool CanDeliver(PlannedAlert alert) => true;

    public async Task<Result<Unit>> SendAsync(PlannedAlert alert, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(Payload(alert, time.GetUtcNow()));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            using var response = await http.PostAsync(new Uri(url.Reveal()), content, cancellationToken);
            return response.IsSuccessStatusCode
                ? Result.Success(Unit.Value)
                : Result.Failure<Unit>(string.Create(CultureInfo.InvariantCulture, $"the webhook answered HTTP {(int)response.StatusCode}"));
        }
        catch (HttpRequestException)
        {
            return Result.Failure<Unit>("could not connect to the webhook");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<Unit>("the webhook did not answer in time");
        }
    }

    internal static byte[] Payload(PlannedAlert alert, DateTimeOffset now)
    {
        var message = AlertMessage.For(alert);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("event", alert.Kind.ToString().ToLowerInvariant());

            writer.WriteString("text", message.Subject);
            writer.WriteString("content", message.Subject);
            writer.WriteString("message", message.Body);
            writer.WriteNumber("thresholdDays", alert.ThresholdDays);

            writer.WriteStartObject("item");
            ItemJson.WriteProperties(writer, alert.Item, now);
            if (alert.PreviousExpiresAt is { } previous)
            {
                writer.WriteString("previousExpiresAt", previous);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
