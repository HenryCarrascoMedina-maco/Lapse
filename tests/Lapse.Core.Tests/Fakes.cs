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

internal sealed class RecordingNotifier(string channel, bool perRecipient = false) : INotifier
{
    public List<AlertDigest> Digests { get; } = [];

    public List<PlannedAlert> Sent => [.. Digests.SelectMany(digest => digest.Alerts)];

    public string? FailWith { get; set; }

    public string Channel => channel;

    public bool IsPerRecipient => perRecipient;

    public bool RequiresEmail { get; init; }

    public int MaxAlertsPerMessage { get; init; } = 100;

    public bool CanDeliver(IReadOnlyList<Owner> recipients) => !RequiresEmail || recipients.Any(owner => owner.Email is not null);

    public Task<Result<Unit>> SendAsync(AlertDigest digest, CancellationToken cancellationToken)
    {
        if (FailWith is { } error)
        {
            return Task.FromResult(Result.Failure<Unit>(error));
        }

        Digests.Add(digest);
        return Task.FromResult(Result.Success(Unit.Value));
    }
}

internal sealed class InMemoryItemStore : IItemStore
{
    private readonly HashSet<AlertRecord> alerts = [];
    private readonly Dictionary<ItemKey, List<DateTimeOffset>> history = [];

    public Dictionary<ItemKey, Item> Items { get; } = [];

    public Task<IReadOnlyDictionary<ItemKey, Item>> GetItemsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ItemKey, Item>>(new Dictionary<ItemKey, Item>(Items));

    public Task ReplaceInventoryAsync(IReadOnlyList<Reconciliation> reconciliations, CancellationToken cancellationToken)
    {
        Items.Clear();
        foreach (var reconciliation in reconciliations)
        {
            Items[reconciliation.Item.Key] = reconciliation.Item;
            if (reconciliation.Observation is { } observation)
            {
                var dates = history.TryGetValue(observation.Key, out var known) ? known : history[observation.Key] = [];
                if (dates.Count == 0 || dates[^1] != observation.ExpiresAt)
                {
                    dates.Add(observation.ExpiresAt);
                }
            }
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

    public Task<DateTimeOffset?> GetRenewedFromAsync(ItemKey key, DateTimeOffset currentExpiry, CancellationToken cancellationToken)
    {
        var previous = history.GetValueOrDefault(key)?.LastOrDefault(date => date != currentExpiry);
        var warned = alerts.Any(record => record.Key == key && record.Kind is AlertKind.Threshold or AlertKind.Expired && record.ExpiresAt == previous);
        return Task.FromResult(previous < currentExpiry && warned ? previous : null);
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
            ["finance"] = new("finance", Email: null, Backup: null),
        },
        AlertPolicy.Default);

    public static WatchTarget Tls(string host, string owner = "ana") => new(new ItemKey(ItemKind.Tls, host), owner);
}
