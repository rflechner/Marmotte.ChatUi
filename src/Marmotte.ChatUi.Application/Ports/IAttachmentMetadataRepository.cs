using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application.Ports;

/// <summary>Stores immutable owned attachment metadata independently of conversation references and file bytes.</summary>
/// <remarks>
/// Insert rejects duplicate IDs with Conflict and foreign owners with NotFound. Find returns null for missing/foreign IDs.
/// Conversation deletion retains metadata; binary lifecycle and metadata cleanup will be defined by the upload adapter.
/// Only trusted server preparation may insert Ready metadata; this port does not validate uploaded bytes.
/// All I/O observes cancellation. No automatic write retries.
/// </remarks>
public interface IAttachmentMetadataRepository
{
    Task<Attachment?> FindAsync(UserId ownerId, Guid attachmentId, CancellationToken cancellationToken);
    Task AddAsync(UserId ownerId, Attachment attachment, CancellationToken cancellationToken);
}
