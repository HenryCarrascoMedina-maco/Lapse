using System.CommandLine;
using Lapse.Cli.Output;
using Lapse.Core.Alerts;

namespace Lapse.Cli.Commands;

internal static class NotifyCommand
{
    public static Command Create()
    {
        var owner = new Option<string?>("--owner") { Description = "Owner who receives the test email. Defaults to defaults.owner." };
        var channel = new Option<string?>("--channel") { Description = "Only test this channel: email, webhook, teams or telegram." };

        var test = new Command("test", "Send a test message through every configured alert channel.") { owner, channel };
        test.SetAction((parse, cancellationToken) => LapseRuntime.RunAsync(parse, runtime =>
            TestAsync(runtime, parse.GetValue(owner), parse.GetValue(channel), cancellationToken), cancellationToken));

        return new Command("notify", "Work with the alert channels.") { test };
    }

    private static async Task<int> TestAsync(LapseRuntime runtime, string? ownerName, string? channel, CancellationToken cancellationToken)
    {
        var notifiers = runtime.Notifiers
            .Where(notifier => channel is null || string.Equals(notifier.Channel, channel, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (notifiers.Count == 0)
        {
            Terminal.Error(channel is null
                ? "No alert channels configured. Add notify.email, webhook, teams or telegram to the configuration."
                : $"The channel '{channel}' is not configured. Configured: {string.Join(", ", runtime.Notifiers.Select(notifier => notifier.Channel))}.");
            return ExitCodes.ConfigurationError;
        }

        ownerName ??= runtime.Configuration.DefaultOwner;
        Owner? recipient = null;
        if (ownerName is not null && !runtime.Plan.Owners.TryGetValue(ownerName, out recipient))
        {
            Terminal.Error($"The owner '{ownerName}' is not defined in owners.");
            return ExitCodes.ConfigurationError;
        }

        var failed = false;
        foreach (var notifier in notifiers)
        {
            IReadOnlyList<Owner> recipients = recipient is null ? [] : [recipient];
            if (notifier.IsPerRecipient && !notifier.CanDeliver(recipients))
            {
                Terminal.Error(recipient is null
                    ? $"{notifier.Channel}: choose who receives the test with --owner, or set defaults.owner."
                    : $"{notifier.Channel}: the owner '{recipient.Name}' has no email address.");
                failed = true;
                continue;
            }

            var result = await notifier.SendAsync(AlertDigest.Test(recipients), cancellationToken);
            if (result.IsSuccess)
            {
                var target = notifier.IsPerRecipient ? $" to {recipient!.Email}" : string.Empty;
                Terminal.Line(Terminal.Paint($"{notifier.Channel}: test message sent{target}.", Tone.Good));
            }
            else
            {
                Terminal.Error($"{notifier.Channel}: {result.Error}");
                failed = true;
            }
        }

        return failed ? ExitCodes.DeliveryFailed : ExitCodes.Success;
    }
}
