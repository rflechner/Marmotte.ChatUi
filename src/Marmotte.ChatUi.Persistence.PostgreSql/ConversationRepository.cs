using System.Data;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Persists whole conversation snapshots using revision comparisons and atomic message/reference replacement.</summary>
/// <remarks>Repeatable-read loading prevents mixed revisions. Navigation never loads message contents.</remarks>
internal sealed class ConversationRepository(Database database) : IConversationRepository
{
    public Task<Conversation?> FindAsync(UserId ownerId, Guid conversationId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        ConversationEntry entry;
        await using (var command = Database.Command(connection,
            "SELECT id, title, created_at, updated_at, revision FROM chat.conversations WHERE owner_id = $1 AND id = $2", ownerId.Value, conversationId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            entry = ReadEntry(reader, ownerId);
        }

        var attachments = new Dictionary<Guid, List<Attachment>>();
        await using (var command = Database.Command(connection, """
            SELECT a.id, a.owner_id, a.file_name, a.media_type, a.size_bytes, a.created_at, a.state, a.width, a.height, r.message_id
            FROM chat.message_attachments r JOIN chat.attachments a ON a.owner_id = r.owner_id AND a.id = r.attachment_id
            WHERE r.owner_id = $1 AND r.conversation_id = $2 ORDER BY r.message_id, r.position
            """, ownerId.Value, conversationId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var messageId = reader.GetGuid(9);
                if (!attachments.TryGetValue(messageId, out var list))
                {
                    list = [];
                    attachments.Add(messageId, list);
                }

                list.Add(AttachmentMetadataRepository.Read(reader));
            }
        }

        var messages = new List<Message>();
        await using (var command = Database.Command(connection, """
            SELECT id, submission_id, sequence, role, content, state, created_at, updated_at, failure
            FROM chat.messages WHERE owner_id = $1 AND conversation_id = $2 ORDER BY sequence
            """, ownerId.Value, conversationId))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(new Message(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), (MessageRole)reader.GetInt32(3),
                    reader.GetString(4), (MessageState)reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetFieldValue<DateTimeOffset>(7), attachments.GetValueOrDefault(reader.GetGuid(0)),
                    reader.IsDBNull(8) ? null : (GenerationFailure)reader.GetInt32(8)));
            }
        }

        var result = Conversation.Restore(entry.Id, ownerId, entry.Title, entry.CreatedAt, entry.UpdatedAt, entry.Revision, messages);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    });

    public Task<Page<ConversationEntry>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection, """
            SELECT id, title, created_at, updated_at, revision FROM chat.conversations
            WHERE owner_id = $1 ORDER BY created_at, id LIMIT $2 OFFSET $3
            """, ownerId.Value, page.Limit + 1, page.Offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<ConversationEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(ReadEntry(reader, ownerId));
        }

        return new Page<ConversationEntry>(entries.Take(page.Limit).ToArray(), entries.Count > page.Limit);
    });

    public Task SaveAsync(UserId ownerId, Conversation conversation, long expectedRevision, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, conversation.OwnerId);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || conversation.Revision != expectedRevision + 1)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = expectedRevision == 0
            ? Database.Command(connection, """
                INSERT INTO chat.conversations (owner_id, id, title, created_at, updated_at, revision)
                VALUES ($1, $2, $3, $4, $5, $6)
                """, ownerId.Value, conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt, conversation.Revision)
            : Database.Command(connection, """
                UPDATE chat.conversations SET title = $3, updated_at = $4, revision = $5
                WHERE owner_id = $1 AND id = $2 AND revision = $6 AND created_at = $7 AND updated_at <= $4
                """, ownerId.Value, conversation.Id, conversation.Title, conversation.UpdatedAt, conversation.Revision, expectedRevision, conversation.CreatedAt))
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new ChatOperationException(ChatErrorCode.Conflict);
            }
        }

        await using (var clear = Database.Command(connection,
            "DELETE FROM chat.messages WHERE owner_id = $1 AND conversation_id = $2", ownerId.Value, conversation.Id))
        {
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var message in conversation.Messages)
        {
            await using var insert = Database.Command(connection, """
                INSERT INTO chat.messages (owner_id, conversation_id, id, submission_id, sequence, role, content, state, failure, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
                """, ownerId.Value, conversation.Id, message.Id, message.SubmissionId, message.Sequence, (int)message.Role,
                message.Content, (int)message.State, Database.Nullable((int?)message.Failure, NpgsqlDbType.Integer), message.CreatedAt, message.UpdatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            for (var position = 0; position < message.Attachments.Count; position++)
            {
                await SaveReferenceAsync(connection, conversation, message, message.Attachments[position], position, cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    public Task DeleteAsync(UserId ownerId, Guid conversationId, long expectedRevision, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Database.Command(connection,
            "DELETE FROM chat.conversations WHERE owner_id = $1 AND id = $2 AND revision = $3", ownerId.Value, conversationId, expectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        return true;
    });

    /// <summary>Maps an owner-filtered navigation row; the reader must contain the five selected columns.</summary>
    private static ConversationEntry ReadEntry(NpgsqlDataReader reader, UserId ownerId) => new(reader.GetGuid(0), ownerId,
        reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetInt64(4));

    /// <summary>Inserts a reference only if existing ready metadata matches the owned snapshot; failure rolls back the entire conversation write.</summary>
    private static async Task SaveReferenceAsync(NpgsqlConnection connection, Conversation conversation, Message message,
        Attachment attachment, int position, CancellationToken cancellationToken)
    {
        await using var command = Database.Command(connection, """
            INSERT INTO chat.message_attachments (owner_id, conversation_id, message_id, attachment_id, position)
            SELECT owner_id, $2, $3, id, $5 FROM chat.attachments
            WHERE owner_id = $1 AND id = $4 AND state = 2 AND file_name = $6 AND media_type = $7
                AND size_bytes = $8 AND created_at = $9 AND width IS NOT DISTINCT FROM $10 AND height IS NOT DISTINCT FROM $11
            """, conversation.OwnerId.Value, conversation.Id, message.Id, attachment.Id, position, attachment.FileName,
            attachment.MediaType, attachment.SizeBytes, attachment.CreatedAt, Database.Nullable(attachment.Width, NpgsqlDbType.Integer),
            Database.Nullable(attachment.Height, NpgsqlDbType.Integer));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
    }
}
