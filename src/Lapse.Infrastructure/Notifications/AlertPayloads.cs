using System.Text.Json;
using Lapse.Core.Alerts;
using Lapse.Infrastructure.Serialization;

namespace Lapse.Infrastructure.Notifications;

internal static class AlertPayloads
{
    public static byte[] Webhook(PlannedAlert alert, DateTimeOffset now) => Json(writer =>
    {
        var message = AlertMessage.For(alert);
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
    });

    public static byte[] Teams(PlannedAlert alert) => Json(writer =>
    {
        var message = AlertMessage.For(alert);
        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteStartArray("attachments");
        writer.WriteStartObject();
        writer.WriteString("contentType", "application/vnd.microsoft.card.adaptive");
        writer.WriteStartObject("content");
        writer.WriteString("$schema", "http://adaptivecards.io/schemas/adaptive-card.json");
        writer.WriteString("type", "AdaptiveCard");
        writer.WriteString("version", "1.4");
        writer.WriteStartArray("body");
        TextBlock(writer, message.Subject, bold: true);
        TextBlock(writer, message.Body, bold: false);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public static byte[] Telegram(PlannedAlert alert, string chatId) => Json(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("chat_id", chatId);
        writer.WriteString("text", AlertMessage.For(alert).Body);
        writer.WriteBoolean("disable_web_page_preview", true);
        writer.WriteEndObject();
    });

    private static void TextBlock(Utf8JsonWriter writer, string text, bool bold)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "TextBlock");
        writer.WriteString("text", text);
        writer.WriteBoolean("wrap", true);
        if (bold)
        {
            writer.WriteString("weight", "Bolder");
        }

        writer.WriteEndObject();
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return buffer.ToArray();
    }
}
