using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Core.Tests;

/// <summary>Verifies conversation use cases, user isolation, submission retries, and response lifecycle through test adapters.</summary>
public sealed class ConversationTests
{
    private readonly TestIdentity identity = new(new UserId("alice"));
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private ConversationService Service => new(identity, store, clock);

    [Fact]
    public async Task Conversation_can_be_created_paged_loaded_and_deleted_through_the_public_contract()
    {
        var service = Service;
        var first = await service.CreateAsync("First", TestContext.Current.CancellationToken);
        clock.Now = clock.Now.AddSeconds(1);
        var second = await service.CreateAsync("Second", TestContext.Current.CancellationToken);
        var page = await service.ListAsync(new PageRequest(limit: 1), TestContext.Current.CancellationToken);
        Assert.Equal(first.Summary.Id, Assert.Single(page.Items).Id);
        Assert.True(page.HasMore);
        var last = await service.ListAsync(new PageRequest(1, 1), TestContext.Current.CancellationToken);
        Assert.Equal(second.Summary.Id, Assert.Single(last.Items).Id);
        Assert.False(last.HasMore);
        Assert.Empty((await service.LoadAsync(first.Summary.Id, TestContext.Current.CancellationToken)).Messages);
        await service.DeleteAsync(first.Summary.Id, TestContext.Current.CancellationToken);
        await AssertError(ChatErrorCode.NotFound, () => service.LoadAsync(first.Summary.Id, TestContext.Current.CancellationToken));
        Assert.Empty((await service.ListAsync(new PageRequest(int.MaxValue), TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task Equal_timestamps_have_stable_paging_without_other_users_records()
    {
        var first = await Service.CreateAsync("One", TestContext.Current.CancellationToken);
        var second = await Service.CreateAsync("Two", TestContext.Current.CancellationToken);
        identity.User = new UserId("bob");
        await Service.CreateAsync("Private", TestContext.Current.CancellationToken);
        identity.User = new UserId("alice");
        var page1 = await Service.ListAsync(new PageRequest(0, 1), TestContext.Current.CancellationToken);
        var page2 = await Service.ListAsync(new PageRequest(1, 1), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { first.Summary.Id, second.Summary.Id }.Order(), new[] { page1.Items[0].Id, page2.Items[0].Id });
    }

    [Fact]
    public async Task Identical_submissions_are_idempotent_but_reused_ids_and_overlapping_generations_conflict()
    {
        var created = await Service.CreateAsync("Chat", TestContext.Current.CancellationToken);
        var request = new RecordMessageRequest(Guid.NewGuid(), "Hello");
        var sent = await Service.RecordMessageAsync(created.Summary.Id, request, TestContext.Current.CancellationToken);
        var retry = await Service.RecordMessageAsync(created.Summary.Id, request, TestContext.Current.CancellationToken);
        Assert.Equal(sent.Summary.Revision, retry.Summary.Revision);
        Assert.Equal(sent.Messages.Select(message => message.Id), retry.Messages.Select(message => message.Id));
        Assert.Equal(new long[] { 1, 2 }, sent.Messages.Select(message => message.Sequence));
        Assert.Equal(GenerationStatus.Pending, sent.Messages[1].Status);
        await AssertError(ChatErrorCode.Conflict, () => Service.RecordMessageAsync(created.Summary.Id,
            request with { Content = "Different" }, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Conflict, () => Service.RecordMessageAsync(created.Summary.Id,
            new RecordMessageRequest(Guid.NewGuid(), "Another"), TestContext.Current.CancellationToken));
        Assert.Equal(2, (await Service.LoadAsync(created.Summary.Id, TestContext.Current.CancellationToken)).Messages.Count);
    }

    [Theory]
    [InlineData(MessageState.Completed, GenerationStatus.Completed)]
    [InlineData(MessageState.Cancelled, GenerationStatus.Cancelled)]
    [InlineData(MessageState.Failed, GenerationStatus.Failed)]
    public async Task Terminal_states_preserve_partial_text_allow_resuming_and_reject_late_deltas(MessageState terminal, GenerationStatus expected)
    {
        var created = await Service.CreateAsync("Chat", TestContext.Current.CancellationToken);
        var request = new RecordMessageRequest(Guid.NewGuid(), "Hello");
        var sent = await Service.RecordMessageAsync(created.Summary.Id, request, TestContext.Current.CancellationToken);
        var responseId = sent.Messages[1].Id;
        clock.Now = clock.Now.AddSeconds(1);
        var streaming = await Service.RecordResponseAsync(created.Summary.Id, responseId,
            new ResponseDelta("Partial", MessageState.Streaming), TestContext.Current.CancellationToken);
        Assert.Equal(GenerationStatus.Streaming, streaming.Messages[1].Status);
        clock.Now = clock.Now.AddSeconds(1);
        var ended = terminal == MessageState.Cancelled
            ? await Service.CancelResponseAsync(created.Summary.Id, responseId, TestContext.Current.CancellationToken)
            : await Service.RecordResponseAsync(created.Summary.Id, responseId,
                new ResponseDelta(string.Empty, terminal, terminal == MessageState.Failed ? GenerationFailure.Interrupted : null), TestContext.Current.CancellationToken);
        Assert.Equal("Partial", ended.Messages[1].Content);
        Assert.Equal(expected, ended.Messages[1].Status);
        Assert.Equal(terminal == MessageState.Failed ? ChatErrorCode.GenerationFailed : (ChatErrorCode?)null, ended.Messages[1].Error);
        await AssertError(ChatErrorCode.Conflict, () => Service.RecordResponseAsync(created.Summary.Id, responseId,
            new ResponseDelta("Late", MessageState.Streaming), TestContext.Current.CancellationToken));
        var retry = await Service.RecordMessageAsync(created.Summary.Id, request, TestContext.Current.CancellationToken);
        Assert.Equal(ended.Summary.Revision, retry.Summary.Revision);
        var resumed = await Service.RecordMessageAsync(created.Summary.Id, new RecordMessageRequest(Guid.NewGuid(), "Continue"), TestContext.Current.CancellationToken);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, resumed.Messages.Select(message => message.Sequence));
        Assert.Equal("Partial", resumed.Messages[1].Content);
        Assert.Equal(string.Empty, sent.Messages[1].Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_conversations_are_hidden_even_if_an_adapter_returns_them(bool faultyAdapter)
    {
        var conversation = await Service.CreateAsync("Private", TestContext.Current.CancellationToken);
        identity.User = new UserId("bob");
        store.ReturnForeignData = faultyAdapter;
        var id = conversation.Summary.Id;
        await AssertError(ChatErrorCode.NotFound, () => Service.LoadAsync(id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Service.DeleteAsync(id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Service.RecordMessageAsync(id,
            new RecordMessageRequest(Guid.NewGuid(), "Intrusion"), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.NotFound, () => Service.CancelResponseAsync(id, Guid.NewGuid(), TestContext.Current.CancellationToken));
        if (faultyAdapter)
        {
            await AssertError(ChatErrorCode.NotFound, () => Service.ListAsync(new PageRequest(), TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Empty((await Service.ListAsync(new PageRequest(), TestContext.Current.CancellationToken)).Items);
        }
    }

    [Fact]
    public async Task Anonymous_access_is_rejected_for_every_conversation_operation()
    {
        identity.User = null;
        var id = Guid.NewGuid();
        await AssertError(ChatErrorCode.Unauthenticated, () => Service.CreateAsync("Chat", TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Service.ListAsync(new PageRequest(), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Service.LoadAsync(id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Service.DeleteAsync(id, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Service.RecordMessageAsync(id, new RecordMessageRequest(id, "Hello"), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Unauthenticated, () => Service.CancelResponseAsync(id, id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_submissions_cannot_overwrite_or_duplicate_a_turn()
    {
        var conversation = await Service.CreateAsync("Chat", TestContext.Current.CancellationToken);
        var bothSaving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        store.BeforeConversationSave = async cancellationToken =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                bothSaving.SetResult();
            }

            await bothSaving.Task.WaitAsync(cancellationToken);
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var first = Service.RecordMessageAsync(conversation.Summary.Id, new RecordMessageRequest(Guid.NewGuid(), "One"), timeout.Token);
        var second = Service.RecordMessageAsync(conversation.Summary.Id, new RecordMessageRequest(Guid.NewGuid(), "Two"), timeout.Token);
        var errors = await Task.WhenAll(CaptureError(first), CaptureError(second));
        Assert.Single(errors, error => error is null);
        Assert.Equal(ChatErrorCode.Conflict, Assert.Single(errors.OfType<ChatOperationException>()).Code);
        var loaded = await Service.LoadAsync(conversation.Summary.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal(2, loaded.Summary.Revision);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_and_does_not_durably_cancel_a_response()
    {
        using var cancellation = new CancellationTokenSource();
        var conversation = await Service.CreateAsync("Chat", cancellation.Token);
        Assert.Equal(cancellation.Token, store.LastToken);
        var sent = await Service.RecordMessageAsync(conversation.Summary.Id,
            new RecordMessageRequest(Guid.NewGuid(), "Hello"), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CancelResponseAsync(conversation.Summary.Id, sent.Messages[1].Id, cancellation.Token));
        Assert.Equal(GenerationStatus.Pending, (await Service.LoadAsync(conversation.Summary.Id, TestContext.Current.CancellationToken)).Messages[1].Status);
        using var duringSave = new CancellationTokenSource();
        store.BeforeConversationSave = token =>
        {
            duringSave.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.RecordResponseAsync(conversation.Summary.Id, sent.Messages[1].Id,
            new ResponseDelta("Unsaved", MessageState.Streaming), duringSave.Token));
        Assert.Empty((await Service.LoadAsync(conversation.Summary.Id, TestContext.Current.CancellationToken)).Messages[1].Content);
    }

    [Fact]
    public async Task Invalid_inputs_and_transitions_do_not_change_persisted_history()
    {
        await AssertError(ChatErrorCode.InvalidRequest, () => Service.CreateAsync(" ", TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.InvalidRequest, () => Service.LoadAsync(Guid.Empty, TestContext.Current.CancellationToken));
        var conversation = await Service.CreateAsync("Chat", TestContext.Current.CancellationToken);
        await AssertError(ChatErrorCode.InvalidRequest, () => Service.RecordMessageAsync(conversation.Summary.Id, null!, TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.InvalidRequest, () => Service.RecordMessageAsync(conversation.Summary.Id,
            new RecordMessageRequest(Guid.Empty, "Hello"), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.InvalidRequest, () => Service.RecordMessageAsync(conversation.Summary.Id,
            new RecordMessageRequest(Guid.NewGuid(), " "), TestContext.Current.CancellationToken));
        var sent = await Service.RecordMessageAsync(conversation.Summary.Id, new RecordMessageRequest(Guid.NewGuid(), "Hello"), TestContext.Current.CancellationToken);
        await AssertError(ChatErrorCode.InvalidRequest, () => Service.RecordResponseAsync(conversation.Summary.Id, sent.Messages[1].Id,
            new ResponseDelta("", MessageState.Failed), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Conflict, () => Service.RecordResponseAsync(conversation.Summary.Id, sent.Messages[0].Id,
            new ResponseDelta("", MessageState.Cancelled), TestContext.Current.CancellationToken));
        await AssertError(ChatErrorCode.Conflict, () => Service.RecordResponseAsync(conversation.Summary.Id, sent.Messages[1].Id,
            new ResponseDelta("", MessageState.Pending), TestContext.Current.CancellationToken));
        Assert.Equal(sent.Summary.Revision, (await Service.LoadAsync(conversation.Summary.Id, TestContext.Current.CancellationToken)).Summary.Revision);
    }

    /// <summary>Asserts that a use case fails with the expected safe application error rather than an adapter exception.</summary>
    internal static async Task AssertError(ChatErrorCode code, Func<Task> action)
    {
        Assert.Equal(code, (await Assert.ThrowsAsync<ChatOperationException>(action)).Code);
    }

    /// <summary>Captures an expected application failure so concurrent outcomes can be compared after both tasks finish.</summary>
    /// <remarks>Successful tasks return null; cancellation and unexpected exceptions still fail the test.</remarks>
    private static async Task<Exception?> CaptureError(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (ChatOperationException exception)
        {
            return exception;
        }
    }
}
