using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Core.Tests;

/// <summary>Verifies domain invariants, immutable history restoration, attachment metadata, and response transitions.</summary>
public sealed class DomainTests
{
    private static readonly UserId Alice = new("alice");
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public void Pages_reject_negative_offsets_and_unbounded_sizes(int offset, int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(offset, limit));
    }

    [Fact]
    public void Ownership_uses_case_sensitive_opaque_ids_and_invalid_values_are_rejected()
    {
        Assert.Equal(Alice, new UserId("alice"));
        Assert.NotEqual(Alice, new UserId("Alice"));
        Assert.Throws<ArgumentException>(() => new UserId(" "));
        Assert.Throws<ArgumentException>(() => Conversation.Create(Guid.Empty, Alice, "Chat", Now));
        Assert.Throws<ArgumentException>(() => Conversation.Create(Guid.NewGuid(), Alice, new string('x', 201), Now));
    }

    [Fact]
    public void History_rehydration_preserves_order_partial_responses_and_revision_without_exposing_mutable_arrays()
    {
        var original = Conversation.Create(Guid.NewGuid(), Alice, "Chat", Now)
            .RecordMessage(Guid.NewGuid(), "Hello", Now);
        original = original.UpdateResponse(original.Messages[1].Id, "Partial", MessageState.Failed, Now.AddSeconds(1), GenerationFailure.Interrupted);
        var messages = original.Messages.ToArray();
        var restored = Conversation.Restore(original.Id, Alice, original.Title, original.CreatedAt, original.UpdatedAt, original.Revision, messages);
        messages[0] = messages[1];
        Assert.Equal(MessageRole.User, restored.Messages[0].Role);
        Assert.Equal("Partial", restored.Messages[1].Content);
        Assert.Equal(original.Revision, restored.Revision);
        Assert.Throws<NotSupportedException>(() => ((IList<Message>)restored.Messages).Clear());
        var resumed = restored.RecordMessage(Guid.NewGuid(), "Next", Now.AddSeconds(2));
        Assert.Equal(4, resumed.Messages.Count);
        Assert.Equal(2, restored.Messages.Count);
    }

    [Fact]
    public void History_rejects_gaps_duplicate_ids_active_earlier_turns_and_backwards_timestamps()
    {
        var conversation = Conversation.Create(Guid.NewGuid(), Alice, "Chat", Now).RecordMessage(Guid.NewGuid(), "Hello", Now);
        var history = conversation.Messages;
        Assert.Throws<ArgumentException>(() => Conversation.Restore(conversation.Id, Alice, "Chat", Now, Now, 1, history));
        Assert.Throws<ArgumentException>(() => Restore([history[0]]));
        Assert.Throws<ArgumentException>(() => Restore([history[0], history[0]]));
        Assert.Throws<ArgumentException>(() => Restore([history[1], history[0]]));
        var next = new Message(Guid.NewGuid(), Guid.NewGuid(), 3, MessageRole.User, "Next", MessageState.Completed, Now, Now);
        var response = new Message(Guid.NewGuid(), next.SubmissionId, 4, MessageRole.Assistant, "", MessageState.Pending, Now, Now);
        Assert.Throws<ArgumentException>(() => Restore([.. history, next, response]));
        var completed = conversation.UpdateResponse(history[1].Id, "Answer", MessageState.Completed, Now);
        var reusedSubmission = new Message(Guid.NewGuid(), history[0].SubmissionId, 3, MessageRole.User, "Next", MessageState.Completed, Now, Now);
        var reusedResponse = new Message(Guid.NewGuid(), history[0].SubmissionId, 4, MessageRole.Assistant, "", MessageState.Pending, Now, Now);
        Assert.Throws<ArgumentException>(() => Restore([.. completed.Messages, reusedSubmission, reusedResponse]));
        Assert.Throws<ArgumentException>(() => conversation.UpdateResponse(history[1].Id, "Hi", MessageState.Streaming, Now.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Conversation.Restore(conversation.Id, Alice, "Chat", Now.AddSeconds(1), Now, 1, []));
    }

    [Fact]
    public void Attachments_require_ready_unique_owned_metadata_and_are_snapshotted()
    {
        var attachment = new Attachment(Guid.NewGuid(), Alice, "picture.png", "image/png", 100, Now, AttachmentState.Ready, 10, 10);
        var attachments = new[] { attachment };
        var conversation = Conversation.Create(Guid.NewGuid(), Alice, "Chat", Now).RecordMessage(Guid.NewGuid(), "", Now, attachments);
        attachments[0] = new Attachment(Guid.NewGuid(), Alice, "other.png", "image/png", 100, Now, AttachmentState.Ready);
        Assert.Equal(attachment.Id, conversation.Messages[0].Attachments[0].Id);
        var empty = Conversation.Create(Guid.NewGuid(), Alice, "Chat", Now);
        var foreign = new Attachment(Guid.NewGuid(), new UserId("bob"), "file.png", "image/png", 100, Now, AttachmentState.Ready);
        var unprepared = new Attachment(Guid.NewGuid(), Alice, "file.pdf", "application/pdf", 100, Now, AttachmentState.Uploaded);
        Assert.Throws<ArgumentException>(() => empty.RecordMessage(Guid.NewGuid(), "", Now, [foreign]));
        Assert.Throws<ArgumentException>(() => empty.RecordMessage(Guid.NewGuid(), "", Now, [unprepared]));
        Assert.Throws<ArgumentException>(() => empty.RecordMessage(Guid.NewGuid(), "", Now, [attachment, attachment]));
        Assert.Throws<ArgumentException>(() => Conversation.Restore(conversation.Id, new UserId("bob"), "Chat", Now, Now, 2, conversation.Messages));
        Assert.Throws<ArgumentException>(() => new Attachment(Guid.NewGuid(), Alice, "../file.png", "image/png", 10, Now, AttachmentState.Ready));
        Assert.Throws<ArgumentException>(() => new Attachment(Guid.NewGuid(), Alice, "file.png", "image/png", 10, Now, AttachmentState.Ready, 10));
    }

    [Theory]
    [InlineData(MessageState.Failed, null)]
    [InlineData(MessageState.Completed, GenerationFailure.Unknown)]
    [InlineData((MessageState)99, null)]
    public void Invalid_response_states_cannot_be_restored(MessageState state, GenerationFailure? failure)
    {
        Assert.Throws<ArgumentException>(() => new Message(Guid.NewGuid(), Guid.NewGuid(), 2, MessageRole.Assistant,
            "", state, Now, Now, failure: failure));
    }

    [Fact]
    public void Non_streaming_completion_and_cancellation_before_first_token_are_supported()
    {
        var conversation = Conversation.Create(Guid.NewGuid(), Alice, "Chat", Now).RecordMessage(Guid.NewGuid(), "Hello", Now);
        var response = conversation.Messages[1].Id;
        Assert.Equal("Answer", conversation.UpdateResponse(response, "Answer", MessageState.Completed, Now).Messages[1].Content);
        Assert.Equal(MessageState.Cancelled, conversation.UpdateResponse(response, "", MessageState.Cancelled, Now).Messages[1].State);
    }

    /// <summary>Attempts history restoration with fixed valid metadata so tests can isolate malformed message sequences.</summary>
    private static Conversation Restore(IEnumerable<Message> messages)
    {
        return Conversation.Restore(Guid.NewGuid(), Alice, "Chat", Now, Now, 3, messages);
    }
}
