using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Stores immutable attachment metadata through EF, never uploaded bytes or filesystem locations.</summary>
internal sealed class AttachmentMetadataRepository(IDbContextFactory<ChatDbContext> contexts) : IAttachmentMetadataRepository
{
    public Task<Attachment?> FindAsync(UserId ownerId, Guid attachmentId, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await context.Attachments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId.Value && item.Id == attachmentId, cancellationToken).ConfigureAwait(false);
        return row?.ToDomain();
    });

    public Task AddAsync(UserId ownerId, Attachment attachment, CancellationToken cancellationToken) => Database.RunAsync(async () =>
    {
        Database.RequireOwner(ownerId, attachment.OwnerId);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Attachments.Add(attachment.ToRow());
        return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    });
}
