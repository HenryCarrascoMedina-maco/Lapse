using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lapse.Infrastructure.Storage;

internal static class SqliteDatabase
{
    private const string MigrationPrefix = "Lapse.Migrations.";

    public static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        CreateOwnerOnlyFile(path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await MigrateAsync(connection, cancellationToken);
        return connection;
    }

    [SuppressMessage("Security", "CA2100", Justification = "Only receives constant SQL; values are always bound as parameters.")]
    public static SqliteCommand Command(
        this SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction = null,
        params ReadOnlySpan<(string Name, object? Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void CreateOwnerOnlyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (OperatingSystem.IsWindows() || File.Exists(path))
        {
            return;
        }

        using var file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

    private static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var create = connection.Command("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)"))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var current = connection.Command("SELECT COALESCE(MAX(version), 0) FROM schema_version");
        var applied = Convert.ToInt32(await current.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        foreach (var (version, sql) in Migrations().Where(migration => migration.Version > applied))
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using (var migrate = connection.Command(sql, transaction))
            {
                await migrate.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record = connection.Command(
                "INSERT INTO schema_version (version, applied_at) VALUES ($version, $appliedAt)",
                transaction,
                ("$version", version),
                ("$appliedAt", Format(DateTimeOffset.UtcNow))))
            {
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static IEnumerable<(int Version, string Sql)> Migrations()
    {
        var assembly = typeof(SqliteDatabase).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(MigrationPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var version = int.Parse(resource.AsSpan(MigrationPrefix.Length, 4), CultureInfo.InvariantCulture);
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            yield return (version, reader.ReadToEnd());
        }
    }
}
