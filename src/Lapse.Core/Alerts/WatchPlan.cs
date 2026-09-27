using Lapse.Core.Items;

namespace Lapse.Core.Alerts;

public sealed record Owner(string Name, string? Email, string? Backup);

public sealed class WatchPlan(
    IReadOnlyList<WatchTarget> targets,
    IReadOnlyDictionary<string, Owner> owners,
    AlertPolicy policy)
{
    public IReadOnlyList<WatchTarget> Targets { get; } = targets;

    public IReadOnlyDictionary<string, Owner> Owners { get; } = owners;

    public AlertPolicy Policy { get; } = policy;

    public IReadOnlyList<Owner> RecipientsFor(string ownerName, bool includeBackup)
    {
        var owner = Owners[ownerName];
        return includeBackup && owner.Backup is { } backup && Owners.TryGetValue(backup, out var backupOwner)
            ? [owner, backupOwner]
            : [owner];
    }
}
