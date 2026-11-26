namespace Marmotte.ChatUi.Domain;

/// <summary>An immutable message owned by a conversation, ordered by a one-based sequence.</summary>
public sealed class Message
{
    /// <summary>Reconstitutes a message and checks role, lifecycle, timestamps, and attachment invariants.</summary>
    /// <remarks>New messages are normally created through <see cref="Conversation.RecordMessage"/>.</remarks>
    public Message(Guid id, Guid submissionId, long sequence, MessageRole role, string content,
        MessageState state, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        IEnumerable<Attachment>? attachments = null, GenerationFailure? failure = null)
    {
        Id = Guard.Id(id, nameof(id));
        SubmissionId = Guard.Id(submissionId, nameof(submissionId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(role) || !Enum.IsDefined(state) || (failure.HasValue && !Enum.IsDefined(failure.Value)))
        {
            throw new ArgumentException("Unknown message role, state, or failure category.");
        }

        if (updatedAt < createdAt || (state == MessageState.Failed) != failure.HasValue)
        {
            throw new ArgumentException("Inconsistent message timestamps or failure category.");
        }

        var copiedAttachments = (attachments ?? []).ToArray();
        if (copiedAttachments.Any(attachment => attachment is null || attachment.State != AttachmentState.Ready)
            || copiedAttachments.Select(attachment => attachment.Id).Distinct().Count() != copiedAttachments.Length)
        {
            throw new ArgumentException("Message attachments must be distinct and ready.", nameof(attachments));
        }

        if (role == MessageRole.User && (state != MessageState.Completed || (string.IsNullOrWhiteSpace(content) && copiedAttachments.Length == 0)))
        {
            throw new ArgumentException("A user message must be complete and contain text or prepared attachments.");
        }

        if (role == MessageRole.Assistant && (copiedAttachments.Length != 0 || (state == MessageState.Pending && content.Length != 0)))
        {
            throw new ArgumentException("Assistant messages cannot have uploads or pending content.");
        }

        Sequence = sequence;
        Role = role;
        Content = content;
        State = state;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
        Attachments = Array.AsReadOnly(copiedAttachments);
        Failure = failure;
    }

    public Guid Id { get; }
    public Guid SubmissionId { get; }
    public long Sequence { get; }
    public MessageRole Role { get; }
    public string Content { get; }
    public MessageState State { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public IReadOnlyList<Attachment> Attachments { get; }
    public GenerationFailure? Failure { get; }
    public bool IsActive => State is MessageState.Pending or MessageState.Streaming;

    /// <summary>Creates the next snapshot of an active assistant response, appending text without changing earlier snapshots.</summary>
    /// <param name="delta">The text to append once, in arrival order; empty text permits a state-only transition.</param>
    /// <param name="state">The next streaming or terminal state.</param>
    /// <param name="now">The update time, which must not precede this message's last update.</param>
    /// <param name="failure">A safe failure category, required only for the Failed state.</param>
    /// <returns>A response snapshot retaining previously received content.</returns>
    /// <exception cref="InvalidOperationException">The message is not an active assistant response or the next state is Pending.</exception>
    /// <exception cref="ArgumentException">The delta, timestamp, state, or failure category is invalid.</exception>
    internal Message Update(string delta, MessageState state, DateTimeOffset now, GenerationFailure? failure)
    {
        if (Role != MessageRole.Assistant || !IsActive)
        {
            throw new InvalidOperationException("Only an active assistant response can change.");
        }

        if (state == MessageState.Pending)
        {
            throw new InvalidOperationException("A response cannot return to pending.");
        }

        if (now < UpdatedAt)
        {
            throw new ArgumentException("Message time cannot move backwards.", nameof(now));
        }

        ArgumentNullException.ThrowIfNull(delta);
        return new Message(Id, SubmissionId, Sequence, Role, Content + delta, state, CreatedAt, now, failure: failure);
    }
}
