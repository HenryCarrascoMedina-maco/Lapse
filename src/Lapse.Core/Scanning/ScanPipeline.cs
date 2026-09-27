using Lapse.Core.Alerts;
using Lapse.Core.Items;

namespace Lapse.Core.Scanning;

public sealed record DeliveryFailure(ItemKey Key, string Channel, string Error);

public sealed record DeliverySummary(int Sent, int Skipped, IReadOnlyList<DeliveryFailure> Failures)
{
    public static DeliverySummary None { get; } = new(0, 0, []);
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
            .SelectMany((target, index) => Reconciler.Reconcile([.. existingByTarget[target.Key]], target, results[index], now))
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

    private Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken) =>
        sources.TryGetValue(target.Key.Kind, out var source)
            ? source.ObserveAsync(target, cancellationToken)
            : Task.FromResult(Result.Failure<IReadOnlyList<Observation>>($"No source is registered for '{target.Key.Kind.Label()}'."));

    private async Task<DeliverySummary> DeliverAsync(
        IEnumerable<Reconciliation> reconciliations,
        WatchPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sent = 0;
        var skipped = 0;
        var failures = new List<DeliveryFailure>();

        var alerts = reconciliations
            .Select(reconciliation => AlertPlanner.Plan(reconciliation, plan, now))
            .OfType<PlannedAlert>();

        foreach (var alert in alerts)
        {
            foreach (var notifier in notifiers.Where(notifier => notifier.CanDeliver(alert)))
            {
                var record = alert.RecordFor(notifier.Channel);
                if (await store.WasAlertSentAsync(record, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                var result = await notifier.SendAsync(alert, cancellationToken);
                if (result.IsSuccess)
                {
                    await store.RecordAlertSentAsync(record, now, cancellationToken);
                    sent++;
                }
                else
                {
                    failures.Add(new DeliveryFailure(alert.Item.Key, notifier.Channel, result.Error));
                }
            }
        }

        return new DeliverySummary(sent, skipped, failures);
    }
}
