using Lapse.Core.Alerts;

namespace Lapse.Infrastructure.Configuration;

public sealed record EmailSettings(string Host, int Port, string? User, Secret? Password, string From, bool UseTls);

public sealed record LapseConfiguration(
    WatchPlan Plan,
    EmailSettings? Email,
    Secret? WebhookUrl,
    string BaseDirectory);
