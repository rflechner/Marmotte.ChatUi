using System.Security.Cryptography;
using System.Text;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Protects credentials with a host-owned persistent key ring and purposes bound to the exact owner/profile.</summary>
/// <remarks>Plaintext never enters SQL parameters. Missing keys or tampering fail closed with a safe storage error.</remarks>
internal sealed class ModelCredentialStore(Database database, IDataProtectionProvider protection) : IModelCredentialStore
{
    public Task<ModelCredential?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection, """
            SELECT c.protected_value FROM chat.profiles p LEFT JOIN chat.profile_credentials c
                ON c.owner_id = p.owner_id AND c.profile_id = p.id
            WHERE p.owner_id = $1 AND p.id = $2
            """, ownerId.Value, profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        if (reader.IsDBNull(0))
        {
            return null;
        }

        try
        {
            var bytes = Protector(ownerId, profileId).Unprotect(reader.GetFieldValue<byte[]>(0));
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
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ConnectionProfileRepository.LockAsync(connection, ownerId, profileId, cancellationToken).ConfigureAwait(false);
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

        await using var command = Database.Command(connection, """
            INSERT INTO chat.profile_credentials (owner_id, profile_id, protected_value) VALUES ($1, $2, $3)
            ON CONFLICT (owner_id, profile_id) DO UPDATE SET protected_value = EXCLUDED.protected_value
            """, ownerId.Value, profileId, ciphertext);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ConnectionProfileRepository.LockAsync(connection, ownerId, profileId, cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection,
            "DELETE FROM chat.profile_credentials WHERE owner_id = $1 AND profile_id = $2", ownerId.Value, profileId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    /// <summary>Separates credential ciphertext from cookies and prevents copying it to another owner or profile.</summary>
    private IDataProtector Protector(UserId ownerId, Guid profileId) => protection
        .CreateProtector("Marmotte.ChatUi.ModelCredentials.v1")
        .CreateProtector(ownerId.Value)
        .CreateProtector(profileId.ToString("D"));
}
