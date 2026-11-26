using System.Runtime.CompilerServices;
using System.Text;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Authorizes and orchestrates text generation, durable checkpoints, cancellation and interrupted-history recovery.</summary>
/// <remarks>Scope with the current user. The host owns injected adapters; share GenerationRegistry across scopes in one process.</remarks>
public sealed class ChatService(ICurrentUser currentUser, IConversationRepository conversations,
    IConnectionProfileRepository profiles, IResponseGenerator generator, IModelCapabilities capabilities,
    GenerationRegistry registry, TimeProvider clock)
{
    /// <summary>Loads owned history and marks an orphaned active response interrupted, preserving its durable text.</summary>
    /// <param name="conversationId">The conversation to authorize and load.</param>
    /// <param name="cancellationToken">Cancels identity resolution, loading and recovery persistence.</param>
    /// <returns>Durable history, including a live response's latest checkpoint if another circuit owns it.</returns>
    /// <exception cref="ChatOperationException">Authentication, missing/foreign data, storage or revision checks fail.</exception>
    /// <remarks>An active reservation in another circuit yields its current persisted snapshot without interrupting it.</remarks>
    public async Task<ConversationView> LoadAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        var conversation = await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false);
        GenerationRegistry.Lease lease;
        try
        {
            lease = registry.Acquire(user, conversationId);
        }
        catch (ChatOperationException exception) when (exception.Code == ChatErrorCode.Conflict)
        {
            return Presentation.Map(conversation);
        }

        using (lease)
        {
            conversation = await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false);
            if (conversation.Messages.Count > 0 && conversation.Messages[^1] is { IsActive: true } response)
            {
                var recovered = conversation.UpdateResponse(response.Id, "", MessageState.Failed,
                    Now(conversation), GenerationFailure.Interrupted);
                await SaveAsync(user, recovered, conversation.Revision, cancellationToken).ConfigureAwait(false);
                conversation = recovered;
            }
        }

        return Presentation.Map(conversation);
    }

    /// <summary>Deletes owned history only when no generation is running in this host process.</summary>
    /// <param name="conversationId">The owned conversation to delete, including its messages.</param>
    /// <param name="cancellationToken">Cancels authorization and storage I/O.</param>
    /// <returns>A task completing when the deletion is acknowledged.</returns>
    /// <exception cref="ChatOperationException">Authorization/storage fails or a running generation or stale revision conflicts.</exception>
    public async Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        using var lease = registry.Acquire(user, conversationId);
        var conversation = await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false);
        await conversations.DeleteAsync(user, conversationId, conversation.Revision, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests a running response to stop and waits for its iterator to finish cleanup and release its reservation.</summary>
    /// <param name="conversationId">The owned conversation whose active request should stop.</param>
    /// <param name="cancellationToken">Cancels authorization and the wait, without undoing a cancellation already requested.</param>
    /// <returns>A task completing after the reservation is released; reload history to confirm durable final state.</returns>
    /// <exception cref="ChatOperationException">Identity resolution or loading the owned conversation fails.</exception>
    /// <remarks>For an orphaned response, LoadAsync recovers history. No model request is restarted. The token cancels the wait.</remarks>
    public async Task CancelAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false);
        await registry.CancelAsync(user, conversationId).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Accepts a text turn once, then yields ordered full snapshots through exactly one terminal response.</summary>
    /// <param name="conversationId">Owned conversation to append to.</param>
    /// <param name="profileId">Owned persisted model settings; no browser-supplied owner or credentials.</param>
    /// <param name="request">Stable submission ID and text. Identical accepted retries never invoke the model again.</param>
    /// <param name="stream">Opt-in streaming, requiring recent capability evidence from the scoped adapter.</param>
    /// <param name="cancellationToken">Stops inference. Durable cancellation uses a separate five-second cleanup deadline.</param>
    /// <returns>The accepted persisted turn first, then previews and durable terminal history.</returns>
    /// <exception cref="ChatOperationException">Authorization, input, capability, lifecycle, storage or concurrency checks fail.</exception>
    /// <remarks>
    /// Enumerate once and dispose. Checkpoints combine deltas every 250ms (on arrival) or 1024 characters; terminal text
    /// is always saved. Earlier failed/cancelled turns are excluded from model context. Uncertain writes are never
    /// replayed; reload reconciles them. Early disposal marks interrupted work, unless identity/storage is unavailable.
    /// </remarks>
    public async IAsyncEnumerable<ConversationView> SendAsync(Guid conversationId, Guid profileId,
        RecordMessageRequest request, bool stream = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireValue(request);
        using var lease = registry.Acquire(user, conversationId);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Token);
        var conversation = await FindAsync(user, conversationId, operation.Token).ConfigureAwait(false);
        var accepted = Access.Validate(() => conversation.RecordMessage(request.SubmissionId, request.Content, Now(conversation)));
        if (ReferenceEquals(accepted, conversation))
        {
            yield return Presentation.Map(conversation);
            yield break;
        }

        Access.RequireId(profileId);
        var profile = await profiles.FindAsync(user, profileId, operation.Token).ConfigureAwait(false)
            ?? throw new ChatOperationException(ChatErrorCode.NotFound);
        Access.RequireOwner(user, profile.OwnerId);
        if (profile.Options.Temperature is > 2 || profile.Model.Any(char.IsControl))
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }

        if (stream && (await capabilities.GetAsync(profile, operation.Token).ConfigureAwait(false)).Streaming != CapabilitySupport.Supported)
        {
            throw new ChatOperationException(ChatErrorCode.UnsupportedCapability);
        }

        // Only complete prior pairs enter model context; partial text stays available in the displayed history.
        var history = conversation.Messages.Chunk(2).Where(pair => pair[1].State == MessageState.Completed)
            .SelectMany(pair => pair).Append(accepted.Messages[^2]).ToArray();
        var responseId = accepted.Messages[^1].Id;
        await SaveAsync(user, accepted, conversation.Revision, operation.Token).ConfigureAwait(false);
        conversation = accepted;
        var pending = new StringBuilder();
        var uncertainWrite = false;
        var terminal = false;
        var checkpointAt = clock.GetTimestamp();
        try
        {
            yield return Presentation.Map(conversation);
            await using var updates = generator.GenerateAsync(new(profile, history, stream), operation.Token)
                .GetAsyncEnumerator(operation.Token);
            while (!terminal)
            {
                ResponseDelta delta;
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    delta = await updates.MoveNextAsync().ConfigureAwait(false)
                        ? updates.Current
                        : new("", MessageState.Failed, GenerationFailure.Interrupted);
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested)
                {
                    delta = new("", MessageState.Cancelled);
                }
                catch (ChatOperationException)
                {
                    throw;
                }
                catch (Exception)
                {
                    delta = new("", MessageState.Failed, GenerationFailure.Interrupted);
                }

                pending.Append(delta.Text);
                var updated = Access.Validate(() => conversation.UpdateResponse(responseId, pending.ToString(),
                    delta.State, Now(conversation), delta.Failure));
                var finished = !updated.Messages[^1].IsActive;
                if (finished || pending.Length >= 1024 || clock.GetElapsedTime(checkpointAt) >= TimeSpan.FromMilliseconds(250))
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    uncertainWrite = true;
                    await SaveAsync(user, updated, conversation.Revision,
                        operation.IsCancellationRequested ? cleanup.Token : operation.Token).ConfigureAwait(false);
                    uncertainWrite = false;
                    conversation = updated;
                    pending.Clear();
                    checkpointAt = clock.GetTimestamp();
                    terminal = finished;
                }
                else
                {
                    await RequireSameUserAsync(user, operation.Token).ConfigureAwait(false);
                }

                yield return Presentation.Map(updated);
            }
        }
        finally
        {
            if (!terminal)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                // Reload before cleanup: an uncertain commit may already contain the last delta or terminal state.
                var stored = await FindAsync(user, conversationId, cleanup.Token).ConfigureAwait(false);
                if (stored.Messages.Count > 0 && stored.Messages[^1] is { IsActive: true } response && response.Id == responseId)
                {
                    var text = !uncertainWrite && stored.Revision == conversation.Revision ? pending.ToString() : "";
                    var cancelled = operation.IsCancellationRequested;
                    var recovered = stored.UpdateResponse(responseId, text, cancelled ? MessageState.Cancelled : MessageState.Failed,
                        Now(stored), cancelled ? null : GenerationFailure.Interrupted);
                    await SaveAsync(user, recovered, stored.Revision, cleanup.Token).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Checks current identity again before every persistence checkpoint, rejecting expiry or identity changes.</summary>
    private async Task RequireSameUserAsync(UserId user, CancellationToken cancellationToken)
    {
        Access.RequireOwner(user, await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Finds an owned aggregate without trusting the repository to authorize its returned snapshot.</summary>
    private async Task<Conversation> FindAsync(UserId user, Guid id, CancellationToken cancellationToken)
    {
        Access.RequireId(id);
        await RequireSameUserAsync(user, cancellationToken).ConfigureAwait(false);
        var result = await conversations.FindAsync(user, id, cancellationToken).ConfigureAwait(false)
            ?? throw new ChatOperationException(ChatErrorCode.NotFound);
        Access.RequireOwner(user, result.OwnerId);
        return result;
    }

    /// <summary>Saves one revision without retrying an uncertain commit or retaining an expired identity.</summary>
    private async Task SaveAsync(UserId user, Conversation conversation, long revision, CancellationToken cancellationToken)
    {
        await RequireSameUserAsync(user, cancellationToken).ConfigureAwait(false);
        await conversations.SaveAsync(user, conversation, revision, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Preserves chronological history if the host clock moves backwards.</summary>
    private DateTimeOffset Now(Conversation conversation) => clock.GetUtcNow() < conversation.UpdatedAt
        ? conversation.UpdatedAt : clock.GetUtcNow();
}
