using System.Runtime.CompilerServices;
using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Core.Tests;

/// <summary>Exercises generation acceptance, checkpoints, isolation and recovery through provider-neutral application services.</summary>
public sealed class ChatTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private readonly TestIdentity identity = new(new UserId("alice"));
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private readonly ControlledGenerator provider = new();
    private readonly GenerationRegistry registry = new();
    private ChatService Chat => new(identity, store, store, provider, provider, registry, clock);
    private ConversationService Conversations => new(identity, store, clock);

    [Fact]
    public async Task Acceptance_precedes_inference_and_terminal_checkpoint_combines_small_deltas_without_duplicate_retries()
    {
        var (conversation, profile) = await CreateAsync();
        var request = new RecordMessageRequest(Guid.NewGuid(), "Question");
        await using (var updates = Chat.SendAsync(conversation, profile, request, true, Token).GetAsyncEnumerator(Token))
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.Equal(GenerationStatus.Pending, updates.Current.Messages[^1].Status);
            Assert.Empty(provider.Requests);
            Assert.True(await updates.MoveNextAsync());
            Assert.Equal("First", updates.Current.Messages[^1].Content);
            Assert.Equal(GenerationStatus.Pending, (await Conversations.LoadAsync(conversation, Token)).Messages[^1].Status);
            Assert.True(await updates.MoveNextAsync());
            Assert.Equal("First last", updates.Current.Messages[^1].Content);
            Assert.Equal(GenerationStatus.Completed, updates.Current.Messages[^1].Status);
            Assert.False(await updates.MoveNextAsync());
        }

        var stored = await Conversations.LoadAsync(conversation, Token);
        Assert.Equal(3, stored.Summary.Revision);
        var repeated = await CollectAsync(Chat.SendAsync(conversation, profile, request, true, Token));
        Assert.Single(repeated);
        Assert.Equal(stored.Messages, repeated[0].Messages);
        Assert.Single(provider.Requests);
        Assert.Single(provider.Requests[0].History);
        Assert.Equal(MessageRole.User, provider.Requests[0].History[0].Role);
    }

    [Fact]
    public async Task Cancellation_from_another_scope_flushes_buffered_text_and_releases_the_conversation()
    {
        var (conversation, profile) = await CreateAsync();
        await using var updates = Chat.SendAsync(conversation, profile, NewMessage(), true, Token).GetAsyncEnumerator(Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.True(await updates.MoveNextAsync());
        var cancellation = Chat.CancelAsync(conversation, Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal(GenerationStatus.Cancelled, updates.Current.Messages[^1].Status);
        Assert.Equal("First", updates.Current.Messages[^1].Content);
        Assert.False(await updates.MoveNextAsync());
        await cancellation;
        var continued = await CollectAsync(Chat.SendAsync(conversation, profile, NewMessage(), true, Token));
        Assert.Equal(4, continued[^1].Messages.Count);
        // The cancelled pair remains in history but is omitted from model context.
        Assert.Single(provider.Requests[^1].History);
    }

    [Fact]
    public async Task Early_disposal_persists_interrupted_partial_text_and_releases_provider_resources()
    {
        var (conversation, profile) = await CreateAsync();
        await using (var updates = Chat.SendAsync(conversation, profile, NewMessage(), true, Token).GetAsyncEnumerator(Token))
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.True(await updates.MoveNextAsync());
        }

        var loaded = await Chat.LoadAsync(conversation, Token);
        Assert.Equal("First", loaded.Messages[^1].Content);
        Assert.Equal(ChatErrorCode.ResponseInterrupted, loaded.Messages[^1].Error);
        Assert.True(provider.Disposed);
        await Chat.DeleteAsync(conversation, Token);
    }

    [Fact]
    public async Task Active_generation_is_not_recovered_or_deleted_by_a_second_circuit()
    {
        var (conversation, profile) = await CreateAsync();
        await using var updates = Chat.SendAsync(conversation, profile, NewMessage(), true, Token).GetAsyncEnumerator(Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal(GenerationStatus.Pending, (await Chat.LoadAsync(conversation, Token)).Messages[^1].Status);
        await ConversationTests.AssertError(ChatErrorCode.Conflict, () => Chat.DeleteAsync(conversation, Token));
        await ConversationTests.AssertError(ChatErrorCode.Conflict, () => CollectAsync(Chat.SendAsync(conversation, profile, NewMessage(), true, Token)));
    }

    [Fact]
    public async Task Fresh_process_recovers_orphaned_history_without_restarting_inference()
    {
        var (conversation, _) = await CreateAsync();
        var pending = await Conversations.RecordMessageAsync(conversation, NewMessage(), Token);
        await Conversations.RecordResponseAsync(conversation, pending.Messages[^1].Id, new("Saved", MessageState.Streaming), Token);
        var recovered = await Chat.LoadAsync(conversation, Token);
        Assert.Equal("Saved", recovered.Messages[^1].Content);
        Assert.Equal(ChatErrorCode.ResponseInterrupted, recovered.Messages[^1].Error);
        Assert.Equal(recovered.Summary.Revision, (await Chat.LoadAsync(conversation, Token)).Summary.Revision);
        Assert.Empty(provider.Requests);
    }

    [Theory]
    [InlineData(GenerationFailure.Unavailable, ChatErrorCode.ModelServerUnavailable)]
    [InlineData(GenerationFailure.Rejected, ChatErrorCode.ModelRequestRejected)]
    [InlineData(GenerationFailure.ContextLimit, ChatErrorCode.ContextLimit)]
    [InlineData(GenerationFailure.Interrupted, ChatErrorCode.ResponseInterrupted)]
    public async Task Provider_failures_are_durable_actionable_and_preserve_partial_output(GenerationFailure failure, ChatErrorCode error)
    {
        var (conversation, profile) = await CreateAsync();
        provider.Terminal = new(" tail", MessageState.Failed, failure);
        var snapshots = await CollectAsync(Chat.SendAsync(conversation, profile, NewMessage(), true, Token));
        Assert.Equal("First tail", snapshots[^1].Messages[^1].Content);
        Assert.Equal(error, (await Chat.LoadAsync(conversation, Token)).Messages[^1].Error);
    }

    [Fact]
    public async Task Missing_terminal_result_is_interrupted_and_nonstreaming_can_complete_directly()
    {
        var (conversation, profile) = await CreateAsync();
        provider.Terminal = null;
        var snapshots = await CollectAsync(Chat.SendAsync(conversation, profile, NewMessage(), true, Token));
        Assert.Equal(ChatErrorCode.ResponseInterrupted, snapshots[^1].Messages[^1].Error);
        provider.Terminal = new("Complete", MessageState.Completed);
        snapshots = await CollectAsync(Chat.SendAsync(conversation, profile, NewMessage(), false, Token));
        Assert.Equal("Complete", snapshots[^1].Messages[^1].Content);
        Assert.False(provider.Requests[^1].Stream);
    }

    [Fact]
    public async Task Unknown_streaming_and_foreign_profiles_do_not_persist_a_turn()
    {
        var (conversation, profile) = await CreateAsync();
        provider.Streaming = CapabilitySupport.Unknown;
        await ConversationTests.AssertError(ChatErrorCode.UnsupportedCapability,
            () => CollectAsync(Chat.SendAsync(conversation, profile, NewMessage(), true, Token)));
        Assert.Empty((await Conversations.LoadAsync(conversation, Token)).Messages);
        identity.User = new UserId("bob");
        var own = await Conversations.CreateAsync("Bob", Token);
        store.ReturnForeignData = true;
        await ConversationTests.AssertError(ChatErrorCode.NotFound,
            () => CollectAsync(Chat.SendAsync(own.Summary.Id, profile, NewMessage(), false, Token)));
        await ConversationTests.AssertError(ChatErrorCode.NotFound, () => Chat.LoadAsync(conversation, Token));
        await ConversationTests.AssertError(ChatErrorCode.NotFound, () => Chat.CancelAsync(conversation, Token));
        await ConversationTests.AssertError(ChatErrorCode.NotFound, () => Chat.DeleteAsync(conversation, Token));
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Expired_identity_cannot_checkpoint_or_continue_generation_and_later_login_recovers_it()
    {
        var (conversation, profile) = await CreateAsync();
        await using (var updates = Chat.SendAsync(conversation, profile, NewMessage(), true, Token).GetAsyncEnumerator(Token))
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.True(await updates.MoveNextAsync());
            identity.User = null;
            await ConversationTests.AssertError(ChatErrorCode.Unauthenticated, () => updates.MoveNextAsync().AsTask());
        }

        identity.User = new UserId("alice");
        Assert.Equal(ChatErrorCode.ResponseInterrupted, (await Chat.LoadAsync(conversation, Token)).Messages[^1].Error);
    }

    [Fact]
    public async Task Uncertain_checkpoint_is_reloaded_without_appending_the_same_delta_twice()
    {
        var (conversation, profile) = await CreateAsync();
        provider.First = new string('x', 1024);
        var uncertain = new UncertainRepository(store);
        var chat = new ChatService(identity, uncertain, store, provider, provider, registry, clock);
        await Assert.ThrowsAsync<IOException>(() => CollectAsync(chat.SendAsync(conversation, profile, NewMessage(), true, Token)));
        var loaded = await Chat.LoadAsync(conversation, Token);
        Assert.Equal(provider.First, loaded.Messages[^1].Content);
        Assert.Equal(ChatErrorCode.ResponseInterrupted, loaded.Messages[^1].Error);
        Assert.Single(provider.Requests);
    }

    /// <summary>Creates owned prerequisites without involving a model server or database.</summary>
    private async Task<(Guid Conversation, Guid Profile)> CreateAsync()
    {
        var conversation = await Conversations.CreateAsync("Chat", Token);
        var profile = await new ConnectionProfileService(identity, store, clock)
            .CreateAsync(new("Model", new Uri("http://localhost/api"), "test"), Token);
        return (conversation.Summary.Id, profile.Id);
    }

    private static RecordMessageRequest NewMessage() => new(Guid.NewGuid(), "Question");

    /// <summary>Fully enumerates the generation under the test deadline; caller assertions inspect observable snapshots.</summary>
    private static async Task<List<ConversationView>> CollectAsync(IAsyncEnumerable<ConversationView> updates)
    {
        var result = new List<ConversationView>();
        await foreach (var update in updates.WithCancellation(Token)) { result.Add(update); }
        return result;
    }

    /// <summary>Produces controlled response lifecycles and records only test request data; disposal simulates transport cleanup.</summary>
    private sealed class ControlledGenerator : IResponseGenerator, IModelCapabilities
    {
        internal List<ResponseRequest> Requests { get; } = [];
        internal string First { get; set; } = "First";
        internal ResponseDelta? Terminal { get; set; } = new(" last", MessageState.Completed);
        internal CapabilitySupport Streaming { get; set; } = CapabilitySupport.Supported;
        internal bool Disposed { get; private set; }
        public Task<ModelCapabilities> GetAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelCapabilities(Streaming, CapabilitySupport.Unknown));
        public async IAsyncEnumerable<ResponseDelta> GenerateAsync(ResponseRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                if (request.Stream)
                {
                    yield return new(First, MessageState.Streaming);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (Terminal is not null)
                {
                    yield return Terminal;
                }
            }
            finally
            {
                Disposed = true;
            }
        }
    }

    /// <summary>Simulates a lost acknowledgement after one committed checkpoint; subsequent cleanup remains available.</summary>
    private sealed class UncertainRepository(TestStore inner) : IConversationRepository
    {
        public Task<Conversation?> FindAsync(UserId ownerId, Guid conversationId, CancellationToken cancellationToken) => inner.FindAsync(ownerId, conversationId, cancellationToken);
        public Task<Page<ConversationEntry>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken) => inner.ListAsync(ownerId, page, cancellationToken);
        public Task DeleteAsync(UserId ownerId, Guid conversationId, long expectedRevision, CancellationToken cancellationToken) => inner.DeleteAsync(ownerId, conversationId, expectedRevision, cancellationToken);
        public async Task SaveAsync(UserId ownerId, Conversation conversation, long expectedRevision, CancellationToken cancellationToken)
        {
            await inner.SaveAsync(ownerId, conversation, expectedRevision, cancellationToken);
            if (conversation.Revision == 3)
            {
                throw new IOException("private storage error");
            }
        }
    }
}
