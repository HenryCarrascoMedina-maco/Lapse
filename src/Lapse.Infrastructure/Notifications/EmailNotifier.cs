using System.Net;
using System.Net.Mail;
using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Infrastructure.Configuration;

namespace Lapse.Infrastructure.Notifications;

public sealed class EmailNotifier(EmailSettings settings) : INotifier
{
    public string Channel => "email";

    public bool CanDeliver(PlannedAlert alert) => alert.Recipients.Any(recipient => recipient.Email is not null);

    public async Task<Result<Unit>> SendAsync(PlannedAlert alert, CancellationToken cancellationToken)
    {
        var message = AlertMessage.For(alert);
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.From),
            Subject = message.Subject,
            Body = message.Body,
        };

        foreach (var address in alert.Recipients.Select(recipient => recipient.Email).OfType<string>().Distinct())
        {
            mail.To.Add(address);
        }

        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = settings.User is null ? null : new NetworkCredential(settings.User, settings.Password?.Reveal()),
        };

        try
        {
            await smtp.SendMailAsync(mail, cancellationToken);
            return Result.Success(Unit.Value);
        }
        catch (SmtpException ex)
        {
            return Result.Failure<Unit>($"the SMTP server rejected the message ({ex.StatusCode})");
        }
    }
}
