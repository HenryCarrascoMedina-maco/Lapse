using System.Globalization;
using System.Text;
using Lapse.Core.Items;

namespace Lapse.Core.Alerts;

public sealed record AlertMessage(string Subject, string Body)
{
    public static AlertMessage For(AlertDigest digest)
    {
        if (digest.IsTest)
        {
            return new AlertMessage(
                "[Lapse] Test message",
                "This is a test message from Lapse. If you can read it, this alert channel works." + Environment.NewLine + Environment.NewLine + "Sent by Lapse.");
        }

        var alerts = Ordered(digest.Alerts);
        var subject = alerts.Count == 1
            ? $"[Lapse] {Headline(alerts[0])}"
            : string.Create(CultureInfo.InvariantCulture, $"[Lapse] {Headline(alerts[0])}, and {alerts.Count - 1} more");

        var body = new StringBuilder();
        if (alerts.Count > 1)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"{alerts.Count} alerts:").AppendLine();
        }

        foreach (var alert in alerts)
        {
            AppendAlert(body, alert);
            body.AppendLine();
        }

        body.Append("Sent by Lapse.");
        return new AlertMessage(subject, body.ToString());
    }

    public static string Headline(PlannedAlert alert)
    {
        var name = alert.Item.Name;
        return alert.Kind switch
        {
            AlertKind.Renewed => $"Renewal verified: {name}",
            AlertKind.Unverified => $"{name} could not be verified",
            AlertKind.Problem => $"{name} has a certificate problem",
            AlertKind.Expired => $"{name} {ExpiredPhrase(alert.DaysRemaining ?? 0)}",
            _ => $"{name} {RemainingPhrase(alert.DaysRemaining ?? 0)}",
        };
    }

    public static string FormatDate(DateTimeOffset date) =>
        date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    // Most urgent first: expired, then by days left; problems, unverified items and renewals after them.
    private static List<PlannedAlert> Ordered(IEnumerable<PlannedAlert> alerts) =>
    [
        .. alerts
            .OrderBy(alert => alert.Kind switch
            {
                AlertKind.Expired or AlertKind.Threshold => 0,
                AlertKind.Problem => 1,
                AlertKind.Unverified => 2,
                _ => 3,
            })
            .ThenBy(alert => alert.DaysRemaining ?? int.MaxValue)
            .ThenBy(alert => alert.Item.Name, StringComparer.Ordinal),
    ];

    private static void AppendAlert(StringBuilder body, PlannedAlert alert)
    {
        var item = alert.Item;
        body.AppendLine(Headline(alert))
            .AppendLine(CultureInfo.InvariantCulture, $"  Item:     {item.Name}")
            .AppendLine(CultureInfo.InvariantCulture, $"  Kind:     {item.Key.Kind.Label()}");

        if (item.Target != item.Key)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"  Source:   {item.Target}");
        }

        if (alert.ExpiresAt is { } expiresAt)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"  Expires:  {FormatDate(expiresAt)}");
        }

        if (alert.Kind == AlertKind.Renewed && alert.PreviousExpiresAt is { } previous)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"  Before:   {FormatDate(previous)}");
        }

        body.AppendLine(CultureInfo.InvariantCulture, $"  Owner:    {item.Owner}");

        if (alert.Kind == AlertKind.Problem && item.Problem is { } problem)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"  Problem:  {problem}");
        }

        if (item.Status == ItemStatus.Failing && item.LastError is { } error)
        {
            var since = item.FailingSince is { } failingSince ? $" since {FormatDate(failingSince)}" : string.Empty;
            body.AppendLine(CultureInfo.InvariantCulture, $"  Warning:  Lapse could not check it{since}: {error}.");
            if (alert.ExpiresAt is not null)
            {
                body.AppendLine("            The expiry date is the last one observed.");
            }
        }

        if (NextStep(alert) is { } next)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"  Next:     {next}");
        }
    }

    private static string? NextStep(PlannedAlert alert) => alert.Kind switch
    {
        AlertKind.Threshold or AlertKind.Expired when alert.Item.Key.Kind == ItemKind.Manual =>
            "Renew it, then update its expiresAt in the configuration.",
        AlertKind.Threshold or AlertKind.Expired =>
            "Renew it and put the new one in place. Lapse confirms the renewal when a scan sees the new date.",
        AlertKind.Unverified =>
            "Check that it can be reached from where Lapse runs, or fix its entry in the configuration.",
        AlertKind.Problem =>
            "Install a certificate issued for this name, together with its full chain.",
        _ => null,
    };

    private static string RemainingPhrase(int days) => days switch
    {
        0 => "expires today",
        1 => "expires in 1 day",
        _ => $"expires in {days} days",
    };

    private static string ExpiredPhrase(int daysRemaining) => -daysRemaining switch
    {
        0 => "expired today",
        1 => "expired 1 day ago",
        var ago => $"expired {ago} days ago",
    };
}
