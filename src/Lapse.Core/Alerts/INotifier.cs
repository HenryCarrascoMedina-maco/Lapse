namespace Lapse.Core.Alerts;

public interface INotifier
{
    string Channel { get; }

    // Email reaches each group of recipients separately; a chat channel is shared, so it gets one message per scan.
    bool IsPerRecipient { get; }

    // Larger digests are split, so a message never exceeds what the channel accepts.
    int MaxAlertsPerMessage { get; }

    bool CanDeliver(IReadOnlyList<Owner> recipients);

    Task<Result<Unit>> SendAsync(AlertDigest digest, CancellationToken cancellationToken);
}
