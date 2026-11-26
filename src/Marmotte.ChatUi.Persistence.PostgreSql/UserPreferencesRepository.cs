using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Domain;
using NpgsqlTypes;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Upserts one preferences snapshot per user while locking selected profiles against concurrent deletion.</summary>
internal sealed class UserPreferencesRepository(Database database) : IUserPreferencesRepository
{
    public Task<UserPreferences?> FindAsync(UserId ownerId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection,
            "SELECT theme, selected_profile_id, selected_model FROM chat.preferences WHERE owner_id = $1", ownerId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new UserPreferences(ownerId, (ThemeMode)reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    });

    public Task SaveAsync(UserId ownerId, UserPreferences preferences, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, preferences.OwnerId);
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (preferences.SelectedProfileId is { } profileId)
        {
            await ConnectionProfileRepository.LockAsync(connection, ownerId, profileId, cancellationToken).ConfigureAwait(false);
        }

        await using var command = Database.Command(connection, """
            INSERT INTO chat.preferences (owner_id, theme, selected_profile_id, selected_model) VALUES ($1, $2, $3, $4)
            ON CONFLICT (owner_id) DO UPDATE SET theme = EXCLUDED.theme,
                selected_profile_id = EXCLUDED.selected_profile_id, selected_model = EXCLUDED.selected_model
            """, ownerId.Value, (int)preferences.Theme, Database.Nullable(preferences.SelectedProfileId, NpgsqlDbType.Uuid), Database.Text(preferences.SelectedModel));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });
}
