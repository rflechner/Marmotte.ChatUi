using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Stores secret-free profiles and serializes deletion with preference and credential writes.</summary>
internal sealed class ConnectionProfileRepository(Database database) : IConnectionProfileRepository
{
    private const string Columns = "id, name, endpoint, model, temperature, max_output_tokens, created_at, updated_at";

    public Task<ConnectionProfile?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection,
            $"SELECT {Columns} FROM chat.profiles WHERE owner_id = $1 AND id = $2", ownerId.Value, profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader, ownerId) : null;
    });

    public Task<Page<ConnectionProfile>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection, $"""
            SELECT {Columns} FROM chat.profiles WHERE owner_id = $1 ORDER BY created_at, id LIMIT $2 OFFSET $3
            """, ownerId.Value, page.Limit + 1, page.Offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var profiles = new List<ConnectionProfile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            profiles.Add(Read(reader, ownerId));
        }

        return new Page<ConnectionProfile>(profiles.Take(page.Limit).ToArray(), profiles.Count > page.Limit);
    });

    public Task SaveAsync(UserId ownerId, ConnectionProfile profile, bool isNew, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, profile.OwnerId);
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection, isNew ? """
            INSERT INTO chat.profiles (owner_id, id, name, endpoint, model, temperature, max_output_tokens, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """ : """
            UPDATE chat.profiles SET name = $3, endpoint = $4, model = $5, temperature = $6, max_output_tokens = $7, updated_at = $9
            WHERE owner_id = $1 AND id = $2 AND created_at = $8
            """, ownerId.Value, profile.Id, profile.Name, profile.Endpoint.AbsoluteUri, profile.Model,
            Database.Nullable(profile.Options.Temperature, NpgsqlDbType.Double),
            Database.Nullable(profile.Options.MaxOutputTokens, NpgsqlDbType.Integer), profile.CreatedAt, profile.UpdatedAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        return true;
    });

    public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockAsync(connection, ownerId, profileId, cancellationToken).ConfigureAwait(false);
        await using (var clear = Database.Command(connection, """
            UPDATE chat.preferences SET selected_profile_id = NULL, selected_model = NULL
            WHERE owner_id = $1 AND selected_profile_id = $2
            """, ownerId.Value, profileId))
        {
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var delete = Database.Command(connection, "DELETE FROM chat.profiles WHERE owner_id = $1 AND id = $2", ownerId.Value, profileId))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    /// <summary>Locks an existing owned profile until the caller's transaction completes; missing/foreign profiles yield NotFound.</summary>
    internal static async Task LockAsync(NpgsqlConnection connection, UserId ownerId, Guid profileId, CancellationToken cancellationToken)
    {
        await using var command = Database.Command(connection,
            "SELECT id FROM chat.profiles WHERE owner_id = $1 AND id = $2 FOR UPDATE", ownerId.Value, profileId);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
    }

    /// <summary>Restores validated settings from the ordered selection; invalid stored values fail loading.</summary>
    private static ConnectionProfile Read(NpgsqlDataReader reader, UserId ownerId) => new(reader.GetGuid(0), ownerId,
        reader.GetString(1), new Uri(reader.GetString(2)), reader.GetString(3),
        new GenerationOptions(reader.IsDBNull(4) ? null : reader.GetDouble(4), reader.IsDBNull(5) ? null : reader.GetInt32(5)),
        reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7));
}
