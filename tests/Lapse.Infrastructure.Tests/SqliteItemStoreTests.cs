using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Storage;

namespace Lapse.Infrastructure.Tests;

public sealed class SqliteItemStoreTests : IDisposable
{
    private static readonly ItemKey Api = new(ItemKind.Tls, "api.example.com:443");
    private static readonly ItemKey Domain = new(ItemKind.Domain, "example.com");

    private readonly TempDirectory directory = new();

    [Fact]
    public async Task Saved_items_are_read_back_intact()
    {
        var api = Item(Api, Dates.Now.AddDays(10));
        var failing = Item(Domain, expiresAt: null) with { Status = ItemStatus.Failing, LastError = "no response" };

        await using (var store = await OpenAsync())
        {
            await store.ReplaceInventoryAsync([Reconciled(api), Reconciled(failing)], CancellationToken.None);
        }

        await using var reopened = await OpenAsync();
        var items = await reopened.GetItemsAsync(CancellationToken.None);

        Assert.Equal(api, items[Api]);
        Assert.Equal(failing, items[Domain]);
    }

    [Fact]
    public async Task Items_missing_from_a_scan_are_removed_with_their_alerts()
    {
        await using var store = await OpenAsync();
        await store.ReplaceInventoryAsync([Reconciled(Item(Api, Dates.Now.AddDays(5))), Reconciled(Item(Domain, Dates.Now.AddDays(5)))], CancellationToken.None);
        var record = new AlertRecord(Domain, AlertKind.Threshold, 7, Dates.Now.AddDays(5), "email");
        await store.RecordAlertSentAsync(record, Dates.Now, CancellationToken.None);

        await store.ReplaceInventoryAsync([Reconciled(Item(Api, Dates.Now.AddDays(5)))], CancellationToken.None);

        Assert.Equal([Api], (await store.GetItemsAsync(CancellationToken.None)).Keys);
        Assert.False(await store.WasAlertSentAsync(record, CancellationToken.None));
    }

    [Fact]
    public async Task Alert_ledger_distinguishes_every_part_of_the_record()
    {
        var expiresAt = Dates.Now.AddDays(5);
        await using var store = await OpenAsync();
        await store.ReplaceInventoryAsync([Reconciled(Item(Api, expiresAt))], CancellationToken.None);
        var sent = new AlertRecord(Api, AlertKind.Threshold, 7, expiresAt, "email");

        await store.RecordAlertSentAsync(sent, Dates.Now, CancellationToken.None);
        await store.RecordAlertSentAsync(sent, Dates.Now, CancellationToken.None);

        Assert.True(await store.WasAlertSentAsync(sent, CancellationToken.None));
        Assert.False(await store.WasAlertSentAsync(sent with { Channel = "webhook" }, CancellationToken.None));
        Assert.False(await store.WasAlertSentAsync(sent with { ThresholdDays = 1 }, CancellationToken.None));
        Assert.False(await store.WasAlertSentAsync(sent with { ExpiresAt = expiresAt.AddDays(90) }, CancellationToken.None));
    }

    [Fact]
    public async Task Opening_an_existing_database_does_not_reapply_migrations()
    {
        await using (await OpenAsync())
        {
        }

        await using var reopened = await OpenAsync();

        Assert.Empty(await reopened.GetItemsAsync(CancellationToken.None));
    }

    public void Dispose() => directory.Dispose();

    private Task<SqliteItemStore> OpenAsync() => SqliteItemStore.OpenAsync(directory.File("lapse.db"), CancellationToken.None);

    private static Item Item(ItemKey key, DateTimeOffset? expiresAt) =>
        new(key, key.Value, "it", expiresAt, ItemStatus.Active, Dates.Now, LastError: null);

    private static Reconciliation Reconciled(Item item) => new(
        item,
        item.Status == ItemStatus.Failing ? ScanOutcome.Failed : ScanOutcome.New,
        PreviousExpiresAt: null,
        item.ExpiresAt is { } expiresAt ? new Observation(item.Name, expiresAt, "abc") : null);
}
