using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Persistence.PostgreSql.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Baselines the two previously shipped SQL schemas into EF history without recreating or copying user tables.</summary>
/// <remarks>One-time compatibility only: verifies the old ledger, columns, and constraint names before recording equivalent EF migrations.</remarks>
internal static class LegacySchemaAdoption
{
    private static readonly LegacyVersion[] KnownVersions =
    [
        new("Marmotte.ChatUi.Persistence.PostgreSql.Migrations.001_history_and_settings.sql", "1659384655CD55A6B625E28387EBF92E6B09E1CC4340638CCE3FEA152358EEC0"),
        new("Marmotte.ChatUi.Persistence.PostgreSql.Migrations.002_protected_credentials.sql", "B5EF662E79A5747484ABB5DA2628C8C0CEA892BB348321118DD9D041B80AA5BA")
    ];

    /// <summary>Adopts a recognized legacy database atomically; fresh databases and previously adopted schemas require no changes.</summary>
    internal static async Task ApplyAsync(ChatDbContext context, CancellationToken cancellationToken)
    {
        var legacyExists = await context.Database.SqlQueryRaw<bool>(
            "SELECT to_regclass('chat.schema_migrations') IS NOT NULL AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        if (!legacyExists)
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(781245906251)", cancellationToken).ConfigureAwait(false);
        var history = context.GetService<IHistoryRepository>();
        // Npgsql probes history by catching a missing-table error, which would abort this PostgreSQL transaction.
        var efHistoryExists = await context.Database.SqlQueryRaw<bool>(
            "SELECT to_regclass('chat.\"__EFMigrationsHistory\"') IS NOT NULL AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        if (efHistoryExists
            && (await history.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count != 0)
        {
            return;
        }

        var versions = await context.Database.SqlQueryRaw<LegacyVersion>(
            "SELECT name AS \"Name\", checksum AS \"Checksum\" FROM chat.schema_migrations ORDER BY name")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (versions.Count is < 1 or > 2 || !versions.SequenceEqual(KnownVersions.Take(versions.Count)))
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }

        await ValidateSchemaAsync(context, versions.Count == 2, cancellationToken).ConfigureAwait(false);
        // EF's foreign-key index convention adds this index to the otherwise equivalent legacy schema.
        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_preferences_owner_id_selected_profile_id"
            ON chat.preferences (owner_id, selected_profile_id)
            """, cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken).ConfigureAwait(false);
        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        Type[] equivalentTypes = [typeof(InitialHistoryAndSettings), typeof(ProtectedModelCredentials)];
        foreach (var type in equivalentTypes.Take(versions.Count))
        {
            var id = migrations.Single(migration => migration.Value.AsType() == type).Key;
            await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(id, "10.0.12")), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rejects missing or structurally changed legacy tables before recording an EF baseline; does not inspect private contents.</summary>
    private static async Task ValidateSchemaAsync(ChatDbContext context, bool includesCredentials, CancellationToken cancellationToken)
    {
        // Compare with the shipped baseline, not today's model: later EF migrations may add or remove columns.
        var baseline = new ProtectedModelCredentials().TargetModel;
        var model = context.GetService<IModelRuntimeInitializer>().Initialize(baseline, designTime: true).GetRelationalModel();
        var tables = model.Tables.Where(table => table.Schema == "chat" && (includesCredentials || table.Name != "profile_credentials")).ToArray();
        var names = tables.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        var expectedColumns = tables.SelectMany(table => table.Columns.Select(column =>
            new LegacyColumn(table.Name, column.Name, column.StoreType, column.IsNullable)));
        var actualColumns = await context.Database.SqlQueryRaw<LegacyColumn>("""
            SELECT c.relname AS "TableName", a.attname AS "Name", format_type(a.atttypid, a.atttypmod) AS "StoreType",
                NOT a.attnotnull AS "IsNullable"
            FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'chat' AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!expectedColumns.OrderBy(column => column.TableName, StringComparer.Ordinal).ThenBy(column => column.Name, StringComparer.Ordinal)
            .SequenceEqual(actualColumns.Where(column => names.Contains(column.TableName))
                .OrderBy(column => column.TableName, StringComparer.Ordinal).ThenBy(column => column.Name, StringComparer.Ordinal)))
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }

        var expectedConstraints = tables.SelectMany(table => table.UniqueConstraints.Select(constraint => constraint.Name)
            .Concat(table.ForeignKeyConstraints.Select(constraint => constraint.Name))
            .Concat(table.CheckConstraints.Select(constraint => constraint.Name))
            .Select(name => new LegacyConstraint(table.Name, name ?? throw new InvalidOperationException("A migration constraint must be named."))));
        var actualConstraints = await context.Database.SqlQueryRaw<LegacyConstraint>("""
            SELECT c.relname AS "TableName", k.conname AS "Name" FROM pg_constraint k
            JOIN pg_class c ON c.oid = k.conrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'chat' AND k.contype IN ('p', 'u', 'f', 'c')
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!expectedConstraints.ToHashSet().SetEquals(actualConstraints.Where(constraint => names.Contains(constraint.TableName))))
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }
    }

    /// <summary>Projects a legacy migration identifier/checksum without mapping its ledger into the code-first model.</summary>
    private sealed record LegacyVersion(string Name, string Checksum);

    /// <summary>Describes only a column's storage shape for non-destructive legacy compatibility checks.</summary>
    private sealed record LegacyColumn(string TableName, string Name, string StoreType, bool IsNullable);

    /// <summary>Identifies a legacy key, relationship, or check constraint without reading user data.</summary>
    private sealed record LegacyConstraint(string TableName, string Name);
}
