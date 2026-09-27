using System.Text.Json;
using Lapse.Core.Alerts;
using Lapse.Core.Items;

namespace Lapse.Infrastructure.Serialization;

public static class ItemJson
{
    public static void WriteProperties(Utf8JsonWriter writer, Item item, DateTimeOffset now)
    {
        writer.WriteString("kind", item.Key.Kind.Label());
        writer.WriteString("key", item.Key.Value);
        writer.WriteString("name", item.Name);
        writer.WriteString("owner", item.Owner);

        if (item.ExpiresAt is { } expiresAt)
        {
            writer.WriteString("expiresAt", expiresAt);
            writer.WriteNumber("daysRemaining", AlertPolicy.DaysRemaining(expiresAt, now));
        }
        else
        {
            writer.WriteNull("expiresAt");
            writer.WriteNull("daysRemaining");
        }
    }
}
