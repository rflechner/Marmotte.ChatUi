using System.Security.Cryptography;
using System.Text;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Persists authenticated ciphertext through EF with purposes bound to the exact owner/profile.</summary>
/// <remarks>Plaintext never enters the change tracker or SQL. Missing keys and tampering fail closed.</remarks>
internal sealed class ModelCredentialStore(IDbContextFactory<ChatDbContext> contexts, IDataProtectionProvider protection) : IModelCredentialStore
{
    public Task<ModelCredential?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var profile = await context.Profiles.AsNoTracking().Where(row => row.OwnerId == ownerId.Value && row.Id == profileId)
            .Select(row => new
            {
                ProtectedValue = context.Credentials.Where(secret => secret.OwnerId == row.OwnerId && secret.ProfileId == row.Id)
                    .Select(secret => secret.ProtectedValue).FirstOrDefault()
            }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        if (profile.ProtectedValue is null)
        {
            return null;
        }

        try
        {
            var bytes = Protector(ownerId, profileId).Unprotect(profile.ProtectedValue);
            try
            {
                return new ModelCredential(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (CryptographicException)
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }
    });

    public Task SaveAsync(UserId ownerId, Guid profileId, ModelCredential credential, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(credential);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ConnectionProfileRepository.LockAsync(context, ownerId, profileId, cancellationToken).ConfigureAwait(false);
        byte[] ciphertext;
        var bytes = Encoding.UTF8.GetBytes(credential.Value);
        try
        {
            ciphertext = Protector(ownerId, profileId).Protect(bytes);
        }
        catch (CryptographicException)
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        var row = await context.Credentials.SingleOrDefaultAsync(item => item.OwnerId == ownerId.Value && item.ProfileId == profileId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            context.Credentials.Add(new ModelCredentialRow { OwnerId = ownerId.Value, ProfileId = profileId, ProtectedValue = ciphertext });
        }
        else
        {
            row.ProtectedValue = ciphertext;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ConnectionProfileRepository.LockAsync(context, ownerId, profileId, cancellationToken).ConfigureAwait(false);
        await context.Credentials.Where(row => row.OwnerId == ownerId.Value && row.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    /// <summary>Separates credential ciphertext from cookies and prevents copying it to another owner or profile.</summary>
    private IDataProtector Protector(UserId ownerId, Guid profileId) => protection
        .CreateProtector("Marmotte.ChatUi.ModelCredentials.v1")
        .CreateProtector(ownerId.Value)
        .CreateProtector(profileId.ToString("D"));
}
