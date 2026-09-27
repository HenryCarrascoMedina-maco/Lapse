using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Microsoft.Data.Sqlite;

namespace Lapse.Infrastructure.Storage;

public sealed class SqliteItemStore : IItemStore, IAsyncDisposable
{
    private const string UpsertItem = """
        INSERT INTO items (kind, key, target_kind, target_key, name, owner, expires_at, status, last_scanned_at, last_error)
        VALUES ($kind, $key, $targetKind, $targetKey, $name, $owner, $expiresAt, $status, $scannedAt, $error)
        ON CONFLICT (kind, key) DO UPDATE SET
            target_kind = excluded.target_kind, target_key = excluded.target_key, name = excluded.name, owner = excluded.owner, expires_at = excluded.expires_at,
            status = excluded.status, last_scanned_at = excluded.last_scanned_at, last_error = excluded.last_error
        RETURNING id
        """;

    private const string AlertFilter = """
        FROM alerts_sent a JOIN items i ON i.id = a.item_id
        WHERE i.kind = $itemKind AND i.key = $key AND a.kind = $alertKind
          AND a.threshold_days = $threshold AND a.expires_at = $expiresAt AND a.channel = $channel
        """;

    private readonly SqliteConnection connection;

    private SqliteItemStore(SqliteConnection connection) => this.connection = connection;

    public static async Task<SqliteItemStore> OpenAsync(string path, CancellationToken cancellationToken) =>
        new(await SqliteDatabase.OpenAsync(path, cancellationToken));

    public async Task<IReadOnlyDictionary<ItemKey, Item>> GetItemsAsync(CancellationToken cancellationToken)
    {
        await using var command = connection.Command(
            "SELECT kind, key, target_kind, target_key, name, owner, expires_at, status, last_scanned_at, last_error FROM items");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var items = new Dictionary<ItemKey, Item>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new Item(
                KeyAt(reader, 0),
                KeyAt(reader, 2),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : SqliteDatabase.Parse(reader.GetString(6)),
                Enum.Parse<ItemStatus>(reader.GetString(7), ignoreCase: true),
                SqliteDatabase.Parse(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9));
            items[item.Key] = item;
        }

        return items;
    }

    public async Task ReplaceInventoryAsync(IReadOnlyList<Reconciliation> reconciliations, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var keptIds = new HashSet<long>();

        foreach (var reconciliation in reconciliations)
        {
            var itemId = await UpsertAsync(reconciliation.Item, transaction, cancellationToken);
            keptIds.Add(itemId);

            if (reconciliation.Observation is { } observation)
            {
                await using var insert = connection.Command(
                    "INSERT INTO observations (item_id, observed_at, expires_at, fingerprint) VALUES ($id, $at, $expiresAt, $fingerprint)",
                    transaction,
                    ("$id", itemId),
                    ("$at", SqliteDatabase.Format(reconciliation.Item.LastScannedAt)),
                    ("$expiresAt", SqliteDatabase.Format(observation.ExpiresAt)),
                    ("$fingerprint", observation.Fingerprint));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await DeleteItemsExceptAsync(keptIds, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> WasAlertSentAsync(AlertRecord record, CancellationToken cancellationToken)
    {
        await using var command = connection.Command($"SELECT 1 {AlertFilter} LIMIT 1", null, AlertParameters(record));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task RecordAlertSentAsync(AlertRecord record, DateTimeOffset sentAt, CancellationToken cancellationToken)
    {
        await using var command = connection.Command(
            """
            INSERT OR IGNORE INTO alerts_sent (item_id, kind, threshold_days, expires_at, channel, sent_at)
            SELECT id, $alertKind, $threshold, $expiresAt, $channel, $sentAt FROM items WHERE kind = $itemKind AND key = $key
            """,
            null,
            [.. AlertParameters(record), ("$sentAt", SqliteDatabase.Format(sentAt))]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private static (string, object?)[] AlertParameters(AlertRecord record) =>
    [
        ("$itemKind", Code(record.Key.Kind)),
        ("$key", record.Key.Value),
        ("$alertKind", Code(record.Kind)),
        ("$threshold", record.ThresholdDays),
        ("$expiresAt", SqliteDatabase.Format(record.ExpiresAt)),
        ("$channel", record.Channel),
    ];

    private static ItemKey KeyAt(SqliteDataReader reader, int ordinal) =>
        new(Enum.Parse<ItemKind>(reader.GetString(ordinal), ignoreCase: true), reader.GetString(ordinal + 1));

    private static string Code<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    private async Task<long> UpsertAsync(Item item, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.Command(
            UpsertItem,
            transaction,
            ("$kind", Code(item.Key.Kind)),
            ("$key", item.Key.Value),
            ("$targetKind", Code(item.Target.Kind)),
            ("$targetKey", item.Target.Value),
            ("$name", item.Name),
            ("$owner", item.Owner),
            ("$expiresAt", item.ExpiresAt is { } expiresAt ? SqliteDatabase.Format(expiresAt) : null),
            ("$status", Code(item.Status)),
            ("$scannedAt", SqliteDatabase.Format(item.LastScannedAt)),
            ("$error", item.LastError));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task DeleteItemsExceptAsync(HashSet<long> keptIds, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var staleIds = new List<long>();
        await using (var select = connection.Command("SELECT id FROM items", transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!keptIds.Contains(reader.GetInt64(0)))
                {
                    staleIds.Add(reader.GetInt64(0));
                }
            }
        }

        foreach (var id in staleIds)
        {
            await using var delete = connection.Command("DELETE FROM items WHERE id = $id", transaction, ("$id", id));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
