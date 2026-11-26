using Marmotte.ChatUi.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Applies the adapter's generated EF migrations before the host accepts requests.</summary>
/// <remarks>EF owns migration history and locking. No EnsureCreated, schema reset, or automatic downgrade is used.</remarks>
public sealed class PostgreSqlMigrations
{
    private readonly IDbContextFactory<ChatDbContext> contexts;

    /// <summary>Uses the internal factory; each migration operation owns and disposes its context.</summary>
    internal PostgreSqlMigrations(IDbContextFactory<ChatDbContext> contexts) => this.contexts = contexts;

    /// <summary>Applies pending code-first migrations to a new or existing database without resetting user data.</summary>
    /// <param name="cancellationToken">Cancels SQL and lock acquisition; the host additionally imposes a startup deadline.</param>
    /// <returns>A task completing after all pending migrations have been applied.</returns>
    /// <exception cref="ChatOperationException">Storage is unavailable or the migration history is incompatible.</exception>
    /// <exception cref="OperationCanceledException">The migration operation was cancelled.</exception>
    public Task ApplyAsync(CancellationToken cancellationToken = default) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await LegacySchemaAdoption.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        var known = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        if (!applied.SequenceEqual(known.Take(applied.Length), StringComparer.Ordinal))
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });
}
