using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Core.Scanning;

namespace Lapse.Core.Tests;

internal sealed class FakeTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeSource(ItemKind kind) : ISource
{
    public Dictionary<string, Result<IReadOnlyList<Observation>>> Results { get; } = [];

    public ItemKind Kind => kind;

    public Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken) =>
        Task.FromResult(Results[target.Key.Value]);

    public void Expires(string target, DateTimeOffset expiresAt) => Yields(target, (target, expiresAt));

    public void Yields(string target, params (string Key, DateTimeOffset ExpiresAt)[] items) =>
        Results[target] = Result.Success<IReadOnlyList<Observation>>(
            [.. items.Select(item => new Observation(new ItemKey(kind, item.Key), item.Key, item.ExpiresAt, Fingerprint: null))]);

    public void Fails(string target, string error) => Results[target] = Result.Failure<IReadOnlyList<Observation>>(error);
}

internal sealed class RecordingNotifier(string channel) : INotifier
{
    public List<PlannedAlert> Sent { get; } = [];

    public string? FailWith { get; set; }

    public string Channel => channel;

    public bool CanDeliver(PlannedAlert alert) => true;

    public Task<Result<Unit>> SendAsync(PlannedAlert alert, CancellationToken cancellationToken)
    {
        if (FailWith is { } error)
        {
            return Task.FromResult(Result.Failure<Unit>(error));
        }

        Sent.Add(alert);
        return Task.FromResult(Result.Success(Unit.Value));
    }
}

internal sealed class InMemoryItemStore : IItemStore
{
    private readonly HashSet<AlertRecord> alerts = [];

    public Dictionary<ItemKey, Item> Items { get; } = [];

    public Task<IReadOnlyDictionary<ItemKey, Item>> GetItemsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ItemKey, Item>>(new Dictionary<ItemKey, Item>(Items));

    public Task ReplaceInventoryAsync(IReadOnlyList<Reconciliation> reconciliations, CancellationToken cancellationToken)
    {
        Items.Clear();
        foreach (var reconciliation in reconciliations)
        {
            Items[reconciliation.Item.Key] = reconciliation.Item;
        }

        return Task.CompletedTask;
    }

    public Task<bool> WasAlertSentAsync(AlertRecord record, CancellationToken cancellationToken) =>
        Task.FromResult(alerts.Contains(record));

    public Task RecordAlertSentAsync(AlertRecord record, DateTimeOffset sentAt, CancellationToken cancellationToken)
    {
        alerts.Add(record);
        return Task.CompletedTask;
    }
}

internal static class Plans
{
    public static readonly DateTimeOffset Today = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    public static WatchPlan For(params WatchTarget[] targets) => new(
        targets,
        new Dictionary<string, Owner>
        {
            ["ana"] = new("ana", "ana@example.com", Backup: "it"),
            ["it"] = new("it", "it@example.com", Backup: null),
        },
        AlertPolicy.Default);

    public static WatchTarget Tls(string host) => new(new ItemKey(ItemKind.Tls, host), "ana");
}
