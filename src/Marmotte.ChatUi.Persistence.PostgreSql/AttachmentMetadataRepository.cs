using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Stores immutable attachment metadata, never uploaded bytes or filesystem locations.</summary>
internal sealed class AttachmentMetadataRepository(Database database) : IAttachmentMetadataRepository
{
    internal const string Columns = "id, owner_id, file_name, media_type, size_bytes, created_at, state, width, height";

    public Task<Attachment?> FindAsync(UserId ownerId, Guid attachmentId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection,
            $"SELECT {Columns} FROM chat.attachments WHERE owner_id = $1 AND id = $2", ownerId.Value, attachmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    });

    public Task AddAsync(UserId ownerId, Attachment attachment, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, attachment.OwnerId);
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection, $"""
            INSERT INTO chat.attachments ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """, attachment.Id, ownerId.Value, attachment.FileName, attachment.MediaType, attachment.SizeBytes,
            attachment.CreatedAt, (int)attachment.State, Database.Nullable(attachment.Width, NpgsqlDbType.Integer),
            Database.Nullable(attachment.Height, NpgsqlDbType.Integer));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    });

    /// <summary>Reconstitutes and validates one metadata row; the reader must be positioned on the declared column order.</summary>
    internal static Attachment Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), new UserId(reader.GetString(1)),
        reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetFieldValue<DateTimeOffset>(5),
        (AttachmentState)reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetInt32(8));
}
