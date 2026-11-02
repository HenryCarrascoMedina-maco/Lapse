using System.Diagnostics.CodeAnalysis;
using Lapse.Core.Alerts;
using Lapse.Core.Items;

namespace Lapse.Core.Scanning;

public sealed record DeliveryFailure(IReadOnlyList<ItemKey> Keys, string Channel, string Error);

// Unreachable lists the items whose alert no configured channel can deliver, such as an owner without email when email is the only channel.
public sealed record DeliverySummary(int Sent, int Skipped, IReadOnlyList<DeliveryFailure> Failures, IReadOnlyList<ItemKey> Unreachable)
{
    public static DeliverySummary None { get; } = new(0, 0, [], []);
}

public sealed record ScanReport(DateTimeOffset ScannedAt, IReadOnlyList<Reconciliation> Entries, DeliverySummary Delivery);

public sealed class ScanPipeline
{
    public const int DefaultMaxParallelism = 8;

    private readonly Dictionary<ItemKind, ISource> sources;
    private readonly List<INotifier> notifiers;
    private readonly IItemStore store;
    private readonly TimeProvider time;
    private readonly int maxParallelism;

    public ScanPipeline(
        IEnumerable<ISource> sources,
        IEnumerable<INotifier> notifiers,
        IItemStore store,
        TimeProvider time,
        int maxParallelism = DefaultMaxParallelism)
    {
        this.sources = sources.ToDictionary(source => source.Kind);
        this.notifiers = [.. notifiers];
        this.store = store;
        this.time = time;
        this.maxParallelism = maxParallelism;
    }

    public bool HasNotifiers => notifiers.Count > 0;

    public async Task<ScanReport> RunAsync(WatchPlan plan, bool notify, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var existing = await store.GetItemsAsync(cancellationToken);
        var results = await ObserveAllAsync(plan.Targets, cancellationToken);

        var existingByTarget = existing.Values.ToLookup(item => item.Target);
        var reconciliations = plan.Targets
            .SelectMany((target, index) => Reconciler.Reconcile(
                [.. existingByTarget[target.Key]],
                target,
                results[index],
                now,
                observation => plan.OwnerFor(target, observation)))
            .ToList();

        await store.ReplaceInventoryAsync(reconciliations, cancellationToken);

        var delivery = notify
            ? await DeliverAsync(reconciliations, plan, now, cancellationToken)
            : DeliverySummary.None;

        return new ScanReport(now, reconciliations, delivery);
    }

    private async Task<Result<IReadOnlyList<Observation>>[]> ObserveAllAsync(IReadOnlyList<WatchTarget> targets, CancellationToken cancellationToken)
    {
        var results = new Result<IReadOnlyList<Observation>>[targets.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(Enumerable.Range(0, targets.Count), options, async (index, token) =>
            results[index] = await ObserveAsync(targets[index], token));

        return results;
    }

    // An unexpected error in one target must not stop the scan, or no other item would be alerted.
    [SuppressMessage("Design", "CA1031", Justification = "The error is reported as the target's result.")]
    private async Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken)
    {
        if (!sources.TryGetValue(target.Key.Kind, out var source))
        {
            return Result.Failure<IReadOnlyList<Observation>>($"No source is registered for '{target.Key.Kind.Label()}'.");
        }

        try
        {
            return await source.ObserveAsync(target, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<IReadOnlyList<Observation>>($"unexpected error: {ex.Message}");
        }
    }

    private async Task<DeliverySummary> DeliverAsync(
        IEnumerable<Reconciliation> reconciliations,
        WatchPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sent = 0;
        var skipped = 0;
        var failures = new List<DeliveryFailure>();

        var alerts = new List<PlannedAlert>();
        foreach (var reconciliation in reconciliations)
        {
            var renewedFrom = reconciliation.Item.ExpiresAt is { } expiresAt
                ? await store.GetRenewedFromAsync(reconciliation.Item.Key, expiresAt, cancellationToken)
                : null;
            alerts.AddRange(AlertPlanner.Plan(reconciliation, plan, now, renewedFrom));
        }

        var unreachable = notifiers.Count == 0
            ? []
            : alerts.Where(alert => !notifiers.Any(notifier => notifier.CanDeliver(alert.Recipients))).Select(alert => alert.Item.Key).Distinct().ToList();

        foreach (var notifier in notifiers)
        {
            var pending = new List<PlannedAlert>();
            foreach (var alert in alerts.Where(alert => notifier.CanDeliver(alert.Recipients)))
            {
                if (await store.WasAlertSentAsync(alert.RecordFor(notifier.Channel), cancellationToken))
                {
                    skipped++;
                }
                else
                {
                    pending.Add(alert);
                }
            }

            foreach (var digest in Digests(notifier, pending))
            {
                var result = await notifier.SendAsync(digest, cancellationToken);
                if (!result.IsSuccess)
                {
                    failures.Add(new DeliveryFailure([.. digest.Alerts.Select(alert => alert.Item.Key)], notifier.Channel, result.Error));
                    continue;
                }

                foreach (var alert in digest.Alerts)
                {
                    await store.RecordAlertSentAsync(alert.RecordFor(notifier.Channel), now, cancellationToken);
                }

                sent += digest.Alerts.Count;
            }
        }

        return new DeliverySummary(sent, skipped, failures, unreachable);
    }

    private static IEnumerable<AlertDigest> Digests(INotifier notifier, List<PlannedAlert> alerts)
    {
        if (alerts.Count == 0)
        {
            return [];
        }

        var groups = notifier.IsPerRecipient
            ? alerts.GroupBy(alert => string.Join(',', alert.Recipients.Select(owner => owner.Name)), StringComparer.Ordinal).Select(group => group.ToList())
            : [alerts];

        return groups.SelectMany(group => group
            .Chunk(Math.Max(1, notifier.MaxAlertsPerMessage))
            .Select(chunk => new AlertDigest([.. chunk.SelectMany(alert => alert.Recipients).Distinct()], chunk)));
    }
}
