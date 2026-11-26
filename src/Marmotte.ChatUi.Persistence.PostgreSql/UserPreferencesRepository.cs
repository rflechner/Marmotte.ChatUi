using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Upserts preferences through EF while serializing creation and protecting selected profiles from deletion.</summary>
internal sealed class UserPreferencesRepository(IDbContextFactory<ChatDbContext> contexts) : IUserPreferencesRepository
{
    public Task<UserPreferences?> FindAsync(UserId ownerId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await context.Preferences.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId.Value, cancellationToken).ConfigureAwait(false);
        return row?.ToDomain();
    });

    public Task SaveAsync(UserId ownerId, UserPreferences preferences, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, preferences.OwnerId);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (preferences.SelectedProfileId is { } profileId)
        {
            await ConnectionProfileRepository.LockAsync(context, ownerId, profileId, cancellationToken).ConfigureAwait(false);
        }

        // Serialize first insertion as well as updates without retrying writes or introducing a synthetic user table.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({ownerId.Value}, 781245906252))", cancellationToken).ConfigureAwait(false);
        var row = await context.Preferences.SingleOrDefaultAsync(item => item.OwnerId == ownerId.Value, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new UserPreferencesRow { OwnerId = ownerId.Value };
            context.Preferences.Add(row);
        }

        row.Theme = (int)preferences.Theme;
        row.SelectedProfileId = preferences.SelectedProfileId;
        row.SelectedModel = preferences.SelectedModel;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });
}
