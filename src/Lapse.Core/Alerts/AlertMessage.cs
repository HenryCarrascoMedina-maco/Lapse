using System.Globalization;
using System.Text;
using Lapse.Core.Items;

namespace Lapse.Core.Alerts;

public sealed record AlertMessage(string Subject, string Body)
{
    public static AlertMessage For(PlannedAlert alert)
    {
        var name = alert.Item.Name;
        var headline = alert.Kind switch
        {
            AlertKind.Renewed => $"Renewal verified: {name}",
            AlertKind.Expired => $"{name} {ExpiredPhrase(alert.DaysRemaining)}",
            _ => $"{name} {RemainingPhrase(alert.DaysRemaining)}",
        };

        var body = new StringBuilder()
            .AppendLine(headline)
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Item:     {name}")
            .AppendLine(CultureInfo.InvariantCulture, $"Kind:     {alert.Item.Key.Kind.Label()}")
            .AppendLine(CultureInfo.InvariantCulture, $"Expires:  {FormatDate(alert.ExpiresAt)}")
            .AppendLine(CultureInfo.InvariantCulture, $"Owner:    {alert.Item.Owner}");

        if (alert.Kind == AlertKind.Renewed && alert.PreviousExpiresAt is { } previous)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"Before:   {FormatDate(previous)}");
        }

        body.AppendLine().Append("Sent by Lapse.");
        return new AlertMessage($"[Lapse] {headline}", body.ToString());
    }

    public static string FormatDate(DateTimeOffset date) =>
        date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

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
