using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application.Ports;

/// <summary>Owned conversation navigation metadata, without materializing message contents.</summary>
public sealed record ConversationEntry(Guid Id, UserId OwnerId, string Title,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision);

/// <summary>Storage for complete immutable conversation snapshots and paged navigation metadata.</summary>
/// <remarks>
/// Every operation filters by the trusted owner, including writes. Missing and foreign records are indistinguishable.
/// List orders by CreatedAt ascending, then Id ascending; offset paging is not a cross-request snapshot.
/// Save atomically compares the stored revision with expectedRevision (0 means insert), writes exactly its successor,
/// and commits all messages and attachment references together. A mismatch throws ChatOperationException(Conflict).
/// Validate attachment ownership/references in the same transaction. Delete uses the same revision check and removes
/// message references atomically; binary retention belongs to the attachment adapter. Never automatically retry writes.
/// All I/O propagates cancellation; cancellation after commit can leave a successful write, so reload before retrying.
/// </remarks>
public interface IConversationRepository
{
    Task<Conversation?> FindAsync(UserId ownerId, Guid conversationId, CancellationToken cancellationToken);
    Task<Page<ConversationEntry>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken);
    Task SaveAsync(UserId ownerId, Conversation conversation, long expectedRevision, CancellationToken cancellationToken);
    Task DeleteAsync(UserId ownerId, Guid conversationId, long expectedRevision, CancellationToken cancellationToken);
}

/// <summary>Owned profile storage, ordered by CreatedAt then Id for paging; Find returns null when absent or foreign.</summary>
/// <remarks>
/// Insert must reject existing IDs. Update must not create a missing record or transfer ownership. Settings updates use
/// last committed write wins. Delete must atomically clear matching profile/model preferences for that owner.
/// Adapters enforce ownership and propagate cancellation on every operation; they never persist credentials here.
/// Missing/foreign update and delete yield NotFound; duplicate insertion yields Conflict.
/// </remarks>
public interface IConnectionProfileRepository
{
    Task<ConnectionProfile?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken);
    Task<Page<ConnectionProfile>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken);
    Task SaveAsync(UserId ownerId, ConnectionProfile profile, bool isNew, CancellationToken cancellationToken);
    Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken);
}

/// <summary>One immutable preferences snapshot per trusted user; missing settings return null.</summary>
/// <remarks>
/// Save is an owned upsert with last committed write wins, and verifies any selected profile belongs to the owner
/// in the same transaction (NotFound otherwise). This closes races with profile deletion. All I/O observes cancellation.
/// </remarks>
public interface IUserPreferencesRepository
{
    Task<UserPreferences?> FindAsync(UserId ownerId, CancellationToken cancellationToken);
    Task SaveAsync(UserId ownerId, UserPreferences preferences, CancellationToken cancellationToken);
}
