namespace Marmotte.ChatUi.Domain;

/// <summary>Immutable aggregate controlling ownership, ordered turns, duplicate submissions, and generation state.</summary>
/// <remarks>Each mutation returns a new revision. Instances are detached snapshots, safe to return from repositories.</remarks>
public sealed class Conversation
{
    /// <summary>Builds a snapshot after a factory or mutation has validated the complete message history.</summary>
    /// <remarks>Takes ownership of the supplied array; callers must not retain it for mutation.</remarks>
    private Conversation(Guid id, UserId ownerId, string title, DateTimeOffset createdAt,
        DateTimeOffset updatedAt, long revision, Message[] messages)
    {
        Id = Guard.Id(id, nameof(id));
        ArgumentNullException.ThrowIfNull(ownerId);
        OwnerId = ownerId;
        Title = Guard.Text(title, 200, nameof(title));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (updatedAt < createdAt)
        {
            throw new ArgumentException("Conversation time cannot move backwards.");
        }

        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
        Revision = revision;
        Messages = Array.AsReadOnly(messages);
    }

    public Guid Id { get; }
    public UserId OwnerId { get; }
    public string Title { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public long Revision { get; }
    public IReadOnlyList<Message> Messages { get; }

    /// <summary>Creates an empty conversation at revision 1. Repositories insert it with expected revision 0.</summary>
    public static Conversation Create(Guid id, UserId ownerId, string title, DateTimeOffset now)
    {
        return new Conversation(id, ownerId, title, now, now, 1, []);
    }

    /// <summary>Restores a complete history, rejecting gaps, duplicate IDs, foreign attachments, and invalid turn order.</summary>
    /// <exception cref="ArgumentException">The snapshot violates a conversation invariant.</exception>
    public static Conversation Restore(Guid id, UserId ownerId, string title, DateTimeOffset createdAt,
        DateTimeOffset updatedAt, long revision, IEnumerable<Message> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var history = messages.ToArray();
        if (history.Length % 2 != 0 || revision < 1L + history.Length / 2 || history.Any(message => message is null)
            || history.Select(message => message.Id).Distinct().Count() != history.Length)
        {
            throw new ArgumentException("History must contain distinct message pairs.", nameof(messages));
        }

        var submissions = new HashSet<Guid>();
        var previousTime = createdAt;
        for (var index = 0; index < history.Length; index += 2)
        {
            var user = history[index];
            var assistant = history[index + 1];
            if (user.Sequence != index + 1 || assistant.Sequence != index + 2
                || user.Role != MessageRole.User || assistant.Role != MessageRole.Assistant
                || user.SubmissionId != assistant.SubmissionId || !submissions.Add(user.SubmissionId)
                || user.CreatedAt < previousTime || assistant.CreatedAt != user.CreatedAt
                || user.UpdatedAt != user.CreatedAt || assistant.UpdatedAt > updatedAt
                || (assistant.IsActive && index != history.Length - 2)
                || user.Attachments.Any(attachment => attachment.OwnerId != ownerId || attachment.CreatedAt > user.CreatedAt))
            {
                throw new ArgumentException("Invalid conversation history.", nameof(messages));
            }

            previousTime = assistant.UpdatedAt;
        }

        return new Conversation(id, ownerId, title, createdAt, updatedAt, revision, history);
    }

    /// <summary>Atomically appends a user message and pending assistant response with the same submission ID.</summary>
    /// <remarks>An identical retry returns this snapshot, even after completion. Reusing an ID for different content conflicts.</remarks>
    /// <exception cref="InvalidOperationException">A different submission is active or the submission ID was reused.</exception>
    public Conversation RecordMessage(Guid submissionId, string content, DateTimeOffset now,
        IEnumerable<Attachment>? attachments = null)
    {
        Guard.Id(submissionId, nameof(submissionId));
        ArgumentNullException.ThrowIfNull(content);
        var copiedAttachments = (attachments ?? []).ToArray();
        if (copiedAttachments.Any(attachment => attachment is null || attachment.OwnerId != OwnerId || attachment.CreatedAt > now))
        {
            throw new ArgumentException("Attachments must belong to the conversation owner and already exist.", nameof(attachments));
        }

        var existing = Messages.FirstOrDefault(message => message.SubmissionId == submissionId && message.Role == MessageRole.User);
        if (existing is not null)
        {
            if (existing.Content != content || !existing.Attachments.Select(attachment => attachment.Id)
                .SequenceEqual(copiedAttachments.Select(attachment => attachment.Id)))
            {
                throw new InvalidOperationException("The submission identifier was already used for different content.");
            }

            return this;
        }

        if (Messages.Any(message => message.IsActive))
        {
            throw new InvalidOperationException("Finish or cancel the active response before sending another message.");
        }

        EnsureTime(now);
        var user = new Message(Guid.NewGuid(), submissionId, Messages.Count + 1L, MessageRole.User, content,
            MessageState.Completed, now, now, copiedAttachments);
        var assistant = new Message(Guid.NewGuid(), submissionId, Messages.Count + 2L, MessageRole.Assistant, string.Empty,
            MessageState.Pending, now, now);
        return new Conversation(Id, OwnerId, Title, CreatedAt, now, checked(Revision + 1), [.. Messages, user, assistant]);
    }

    /// <summary>Appends a delta and changes the active response state; cancellation/failure preserve all partial text.</summary>
    /// <remarks>Terminal responses are immutable. Late updates and unknown response IDs fail without changing this snapshot.</remarks>
    public Conversation UpdateResponse(Guid messageId, string delta, MessageState state, DateTimeOffset now,
        GenerationFailure? failure = null)
    {
        EnsureTime(now);
        var index = Messages.ToList().FindIndex(message => message.Id == messageId);
        if (index < 0)
        {
            throw new InvalidOperationException("The response does not exist in this conversation.");
        }

        var messages = Messages.ToArray();
        messages[index] = messages[index].Update(delta, state, now, failure);
        return new Conversation(Id, OwnerId, Title, CreatedAt, now, checked(Revision + 1), messages);
    }

    /// <summary>Prevents a mutation from placing the conversation's update time before its existing history.</summary>
    /// <exception cref="ArgumentException">The proposed timestamp precedes the current update time.</exception>
    private void EnsureTime(DateTimeOffset now)
    {
        if (now < UpdatedAt)
        {
            throw new ArgumentException("Conversation time cannot move backwards.", nameof(now));
        }
    }
}
