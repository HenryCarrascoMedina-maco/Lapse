using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Infrastructure.Configuration;

namespace Lapse.Infrastructure.Notifications;

public sealed class EmailNotifier(EmailSettings settings) : INotifier
{
    public string Channel => "email";

    public bool IsPerRecipient => true;

    public int MaxAlertsPerMessage => 50;

    public bool CanDeliver(IReadOnlyList<Owner> recipients) => recipients.Any(recipient => recipient.Email is not null);

    public async Task<Result<Unit>> SendAsync(AlertDigest digest, CancellationToken cancellationToken)
    {
        var message = AlertMessage.For(digest);
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.From),
            Subject = message.Subject,
            Body = message.Body,
        };

        foreach (var address in digest.Recipients.Select(recipient => recipient.Email).OfType<string>().Distinct())
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
            return Result.Failure<Unit>(Describe(ex));
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or InvalidOperationException)
        {
            return Result.Failure<Unit>($"could not connect to the SMTP server {settings.Host}:{settings.Port}");
        }
    }

    private string Describe(SmtpException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case SocketException socket:
                    return $"could not connect to the SMTP server {settings.Host}:{settings.Port} ({socket.SocketErrorCode})";
                case AuthenticationException:
                    return $"the TLS connection to {settings.Host}:{settings.Port} failed; check \"port\" and \"useTls\"";
            }
        }

        // 530 means "authentication or TLS required"; 534 and 535 mean the credentials were rejected.
        return (int)ex.StatusCode switch
        {
            530 => "the SMTP server requires authentication or TLS; check \"user\", \"password\" and \"useTls\"",
            534 or 535 => "the SMTP server rejected the user or password",
            550 or 553 => "the SMTP server rejected the sender or a recipient address",
            -1 => $"could not connect to the SMTP server {settings.Host}:{settings.Port}",
            var code => $"the SMTP server rejected the message ({code}): {ex.Message}",
        };
    }
}
