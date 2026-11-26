using System.Data;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Persists whole conversation snapshots through EF with owner/revision predicates and atomic message replacement.</summary>
/// <remarks>Repeatable-read loading prevents mixed revisions. Navigation never loads message contents.</remarks>
internal sealed class ConversationRepository(IDbContextFactory<ChatDbContext> contexts) : IConversationRepository
{
    public Task<Conversation?> FindAsync(UserId ownerId, Guid conversationId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        var row = await context.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId.Value && item.Id == conversationId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var references = await (from reference in context.MessageAttachments.AsNoTracking()
                                join attachment in context.Attachments.AsNoTracking()
                                    on new { reference.OwnerId, Id = reference.AttachmentId } equals new { attachment.OwnerId, attachment.Id }
                                where reference.OwnerId == ownerId.Value && reference.ConversationId == conversationId
                                orderby reference.Position
                                select new { reference.MessageId, Attachment = attachment })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var attachments = references.ToLookup(reference => reference.MessageId, reference => reference.Attachment.ToDomain());
        var rows = await context.Messages.AsNoTracking()
            .Where(item => item.OwnerId == ownerId.Value && item.ConversationId == conversationId)
            .OrderBy(item => item.Sequence).ToListAsync(cancellationToken).ConfigureAwait(false);
        var messages = rows.Select(message => new Message(message.Id, message.SubmissionId, message.Sequence,
            (MessageRole)message.Role, message.Content, (MessageState)message.State, message.CreatedAt, message.UpdatedAt,
            attachments[message.Id], (GenerationFailure?)message.Failure));
        var result = Conversation.Restore(row.Id, ownerId, row.Title, row.CreatedAt, row.UpdatedAt, row.Revision, messages);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    });

    public Task<Page<ConversationEntry>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entries = await context.Conversations.AsNoTracking().Where(row => row.OwnerId == ownerId.Value)
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Skip(page.Offset).Take(page.Limit + 1)
            .Select(row => new ConversationEntry(row.Id, ownerId, row.Title, row.CreatedAt, row.UpdatedAt, row.Revision))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<ConversationEntry>(entries.Take(page.Limit).ToArray(), entries.Count > page.Limit);
    });

    public Task SaveAsync(UserId ownerId, Conversation conversation, long expectedRevision, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, conversation.OwnerId);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || conversation.Revision != expectedRevision + 1)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (expectedRevision == 0)
        {
            context.Conversations.Add(new ConversationRow
            {
                Id = conversation.Id,
                OwnerId = ownerId.Value,
                Title = conversation.Title,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt,
                Revision = conversation.Revision
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // ExecuteUpdate bypasses tracking; its explicit revision predicate is the compare-and-swap.
            var affected = await context.Conversations
                .Where(row => row.OwnerId == ownerId.Value && row.Id == conversation.Id && row.Revision == expectedRevision
                    && row.CreatedAt == conversation.CreatedAt && row.UpdatedAt <= conversation.UpdatedAt)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Title, conversation.Title)
                    .SetProperty(row => row.UpdatedAt, conversation.UpdatedAt)
                    .SetProperty(row => row.Revision, conversation.Revision), cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new ChatOperationException(ChatErrorCode.Conflict);
            }
        }

        await context.Messages.Where(row => row.OwnerId == ownerId.Value && row.ConversationId == conversation.Id)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        foreach (var message in conversation.Messages)
        {
            context.Messages.Add(new MessageRow
            {
                OwnerId = ownerId.Value,
                ConversationId = conversation.Id,
                Id = message.Id,
                SubmissionId = message.SubmissionId,
                Sequence = message.Sequence,
                Role = (int)message.Role,
                Content = message.Content,
                State = (int)message.State,
                Failure = (int?)message.Failure,
                CreatedAt = message.CreatedAt,
                UpdatedAt = message.UpdatedAt
            });
            for (var position = 0; position < message.Attachments.Count; position++)
            {
                await AddReferenceAsync(context, conversation, message, message.Attachments[position], position, cancellationToken).ConfigureAwait(false);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    });

    public Task DeleteAsync(UserId ownerId, Guid conversationId, long expectedRevision, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var affected = await context.Conversations.Where(row => row.OwnerId == ownerId.Value && row.Id == conversationId && row.Revision == expectedRevision)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        return true;
    });

    /// <summary>Checks existing immutable metadata before tracking an owned reference; failure rolls back the whole conversation.</summary>
    private static async Task AddReferenceAsync(ChatDbContext context, Conversation conversation, Message message,
        Attachment attachment, int position, CancellationToken cancellationToken)
    {
        var exists = await context.Attachments.AnyAsync(row => row.OwnerId == conversation.OwnerId.Value && row.Id == attachment.Id
            && row.State == (int)AttachmentState.Ready && row.FileName == attachment.FileName && row.MediaType == attachment.MediaType
            && row.SizeBytes == attachment.SizeBytes && row.CreatedAt == attachment.CreatedAt
            && row.Width == attachment.Width && row.Height == attachment.Height, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        context.MessageAttachments.Add(new MessageAttachmentRow
        {
            OwnerId = conversation.OwnerId.Value,
            ConversationId = conversation.Id,
            MessageId = message.Id,
            AttachmentId = attachment.Id,
            Position = position
        });
    }
}
