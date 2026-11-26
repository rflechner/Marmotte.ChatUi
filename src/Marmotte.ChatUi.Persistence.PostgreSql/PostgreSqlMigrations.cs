using System.Security.Cryptography;
using System.Text;
using Marmotte.ChatUi.Contracts;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Applies embedded, ordered SQL migrations atomically under a database-wide advisory lock.</summary>
/// <remarks>Resolved from DI by the host before serving requests. Existing checksums and unknown versions are rejected.</remarks>
public sealed class PostgreSqlMigrations
{
    private readonly Database database;

    /// <summary>Uses the adapter-owned pool; construction and lifetime belong to the composition extension.</summary>
    internal PostgreSqlMigrations(Database database) => this.database = database;

    /// <summary>Upgrades a new or existing database without resetting data; safe to run again or concurrently.</summary>
    /// <param name="cancellationToken">Cancels lock acquisition, SQL, and commit; the caller should also set a startup deadline.</param>
    /// <returns>A task completing only after all migrations commit.</returns>
    /// <exception cref="ChatOperationException">Storage is unavailable or the migration history is incompatible.</exception>
    /// <exception cref="OperationCanceledException">Startup was cancelled. Uncommitted DDL is rolled back.</exception>
    public Task ApplyAsync(CancellationToken cancellationToken = default) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var initialize = Database.Command(connection, """
            SELECT pg_advisory_xact_lock(781245906251);
            CREATE SCHEMA IF NOT EXISTS chat;
            CREATE TABLE IF NOT EXISTS chat.schema_migrations (
                name text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now()
            );
            """))
        {
            await initialize.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var history = Database.Command(connection, "SELECT name, checksum FROM chat.schema_migrations"))
        await using (var reader = await history.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                applied.Add(reader.GetString(0), reader.GetString(1));
            }
        }

        var assembly = typeof(PostgreSqlMigrations).Assembly;
        var names = assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        if (applied.Keys.Except(names, StringComparer.Ordinal).Any())
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }

        var encounteredPending = false;
        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var text = new StreamReader(stream);
            var sql = await text.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            if (applied.TryGetValue(name, out var previous))
            {
                if (previous != checksum || encounteredPending)
                {
                    throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
                }

                continue;
            }

            encounteredPending = true;
            await using var migration = Database.Command(connection, sql);
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var record = Database.Command(connection,
                "INSERT INTO chat.schema_migrations (name, checksum) VALUES ($1, $2)", name, checksum);
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });
}
