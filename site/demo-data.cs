using System.Globalization;
using System.Text.Json;

var now = DateTimeOffset.UtcNow;

(string Kind, string Name, string Owner, int? Days, string Status, string? Note)[] items =
[
    ("tls", "intranet.acme-demo.local:443", "it", -2, "expired", null),
    ("tls", "api.acme-demo.com:443", "platform", 5, "critical", null),
    ("entra", "ERP Sync · secret prod", "ana", 6, "critical", null),
    ("tls", "smtp://mail.acme-demo.com:587", "it", 19, "warning", null),
    ("domain", "acme-demo.pe", "finance", 27, "warning", null),
    ("tls", "vpn.acme-demo.com:443", "it", null, "error", "no response within 10 s"),
    ("tls", "acme-demo.com:443", "it", 88, "renewed", $"was: {Date(3)}"),
    ("file", "invoicing.cer", "accounting", 65, "ok", null),
    ("manual", "Antivirus license", "it", 125, "ok", null),
    ("tls", "postgres://db.acme-demo.com:5432", "platform", 140, "ok", null),
    ("saml", "idp.acme-demo.com · signing certificate 1A2B3C4D", "it", 214, "ok", null),
    ("entra", "Customer portal · certificate CN=portal", "ana", 301, "ok", null),
    ("domain", "acme-demo.com", "finance", 319, "ok", null),
];

await using var output = File.Create(args[0]);
await using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
writer.WriteStartArray();
foreach (var (kind, name, owner, days, status, note) in items)
{
    writer.WriteStartObject();
    writer.WriteString("kind", kind);
    writer.WriteString("key", name);
    writer.WriteString("name", name);
    writer.WriteString("owner", owner);
    if (days is { } remaining)
    {
        writer.WriteString("expiresAt", now.AddDays(remaining).AddHours(2));
    }
    else
    {
        writer.WriteNull("expiresAt");
    }

    writer.WriteString("status", status);
    writer.WriteString("note", note);
    writer.WriteEndObject();
}

writer.WriteEndArray();

string Date(int days) => now.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
