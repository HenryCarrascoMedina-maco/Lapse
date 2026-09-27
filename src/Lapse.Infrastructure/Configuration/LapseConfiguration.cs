using Lapse.Core.Alerts;
using Lapse.Infrastructure.Entra;

namespace Lapse.Infrastructure.Configuration;

public sealed record EmailSettings(string Host, int Port, string? User, Secret? Password, string From, bool UseTls);

public sealed record TelegramSettings(Secret BotToken, string ChatId);

public sealed record LapseConfiguration(
    WatchPlan Plan,
    EmailSettings? Email,
    Secret? WebhookUrl,
    Secret? TeamsUrl,
    TelegramSettings? Telegram,
    IReadOnlyList<EntraTenant> EntraTenants,
    string BaseDirectory);
