namespace Marmotte.ChatUi.Contracts;

/// <summary>The speaker displayed for a conversation message, independently of provider-specific roles.</summary>
public enum ChatRole
{
    User,
    Assistant
}

/// <summary>The response lifecycle shown by the UI, including terminal states that may retain partial content.</summary>
public enum GenerationStatus
{
    Pending,
    Streaming,
    Completed,
    Cancelled,
    Failed
}

/// <summary>The attachment preparation state displayed by the UI; readiness does not establish model support.</summary>
public enum AttachmentStatus
{
    Uploaded,
    Preparing,
    Ready,
    Failed
}

/// <summary>Display metadata only. A host must authorize content retrieval separately; no storage URL is exposed.</summary>
public sealed record AttachmentView(Guid Id, string FileName, string MediaType, long SizeBytes,
    AttachmentStatus Status, int? Width, int? Height);

/// <summary>A detached message snapshot; failed/cancelled responses may contain partial text.</summary>
public sealed record MessageView(Guid Id, Guid SubmissionId, long Sequence, ChatRole Role, string Content,
    GenerationStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<AttachmentView> Attachments, ChatErrorCode? Error);

/// <summary>Conversation metadata for navigation, excluding owner identity and message bodies.</summary>
public sealed record ConversationSummary(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision);

/// <summary>A complete, ordered conversation snapshot. Revision detects stale writes in server adapters.</summary>
public sealed record ConversationView(ConversationSummary Summary, IReadOnlyList<MessageView> Messages);

/// <summary>An incremental UI event. Apply each delta once in order; terminal updates retain previously displayed text.</summary>
/// <remarks>On reconnect, reload the persisted snapshot instead of replaying unacknowledged deltas.</remarks>
public sealed record GenerationUpdate(Guid ConversationId, Guid MessageId, string TextDelta,
    GenerationStatus Status, ChatErrorCode? Error = null);

/// <summary>A text submission. Reuse SubmissionId only when retrying the same content in the same conversation.</summary>
/// <remarks>Attachment uploads are not implemented in M2; attachment metadata is available for future presentation.</remarks>
public sealed record RecordMessageRequest(Guid SubmissionId, string Content);

/// <summary>Host-supplied conversation operations for components; requests never accept an owner identifier.</summary>
/// <remarks>
/// Calls require a trusted current user. Missing/foreign resources yield NotFound; invalid input yields InvalidRequest;
/// stale writes, duplicate ID misuse, and lifecycle violations yield Conflict. All I/O observes cancellation via
/// OperationCanceledException. Cancelling a call is distinct from durably cancelling a response with CancelResponseAsync.
/// </remarks>
public interface IConversationService
{
    Task<ConversationView> CreateAsync(string title, CancellationToken cancellationToken = default);
    Task<Page<ConversationSummary>> ListAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task<ConversationView> LoadAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<ConversationView> RecordMessageAsync(Guid conversationId, RecordMessageRequest request, CancellationToken cancellationToken = default);
    Task<ConversationView> CancelResponseAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);
}
