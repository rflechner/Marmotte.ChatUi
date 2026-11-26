using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Stores secret-free profiles through EF and serializes deletion with selection and credential writes.</summary>
internal sealed class ConnectionProfileRepository(IDbContextFactory<ChatDbContext> contexts) : IConnectionProfileRepository
{
    public Task<ConnectionProfile?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await context.Profiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId.Value && item.Id == profileId, cancellationToken).ConfigureAwait(false);
        return row?.ToDomain();
    });

    public Task<Page<ConnectionProfile>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await context.Profiles.AsNoTracking().Where(row => row.OwnerId == ownerId.Value)
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Skip(page.Offset).Take(page.Limit + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<ConnectionProfile>(rows.Take(page.Limit).Select(row => row.ToDomain()).ToArray(), rows.Count > page.Limit);
    });

    public Task SaveAsync(UserId ownerId, ConnectionProfile profile, bool isNew, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, profile.OwnerId);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (isNew)
        {
            context.Profiles.Add(profile.ToRow());
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var affected = await context.Profiles.Where(row => row.OwnerId == ownerId.Value && row.Id == profile.Id && row.CreatedAt == profile.CreatedAt)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, profile.Name)
                    .SetProperty(row => row.Endpoint, profile.Endpoint.AbsoluteUri)
                    .SetProperty(row => row.Model, profile.Model)
                    .SetProperty(row => row.Temperature, profile.Options.Temperature)
                    .SetProperty(row => row.MaxOutputTokens, profile.Options.MaxOutputTokens)
                    .SetProperty(row => row.UpdatedAt, profile.UpdatedAt), cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new ChatOperationException(ChatErrorCode.NotFound);
            }
        }

        return true;
    });

    public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockAsync(context, ownerId, profileId, cancellationToken).ConfigureAwait(false);
        await context.Preferences.Where(row => row.OwnerId == ownerId.Value && row.SelectedProfileId == profileId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SelectedProfileId, (Guid?)null)
                .SetProperty(row => row.SelectedModel, (string?)null), cancellationToken).ConfigureAwait(false);
        await context.Profiles.Where(row => row.OwnerId == ownerId.Value && row.Id == profileId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    /// <summary>Locks an owned profile for the caller's transaction; PostgreSQL row locking has no EF LINQ equivalent.</summary>
    /// <remarks>Parameters remain bound by EF. Missing/foreign profiles yield NotFound.</remarks>
    internal static async Task LockAsync(ChatDbContext context, UserId ownerId, Guid profileId, CancellationToken cancellationToken)
    {
        var rows = await context.Profiles.FromSqlInterpolated($"SELECT * FROM chat.profiles WHERE owner_id = {ownerId.Value} AND id = {profileId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
    }
}
