using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Authorized conversation use cases; compose with request/user-scoped identity and repositories in the host.</summary>
/// <remarks>Does not call a model. ChatService orchestrates model generation and checkpointing.</remarks>
public sealed class ConversationService(ICurrentUser currentUser, IConversationRepository repository, TimeProvider timeProvider)
    : IConversationService
{
    public async Task<ConversationView> CreateAsync(string title, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        var conversation = Access.Validate(() => Conversation.Create(Guid.NewGuid(), user, title, timeProvider.GetUtcNow()));
        await repository.SaveAsync(user, conversation, 0, cancellationToken).ConfigureAwait(false);
        return Presentation.Map(conversation);
    }

    public async Task<Page<ConversationSummary>> ListAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireValue(page);
        var result = await repository.ListAsync(user, page, cancellationToken).ConfigureAwait(false);
        foreach (var item in result.Items)
        {
            Access.RequireOwner(user, item.OwnerId);
        }

        return new Page<ConversationSummary>(Array.AsReadOnly(result.Items.Select(item => new ConversationSummary(
            item.Id, item.Title, item.CreatedAt, item.UpdatedAt, item.Revision)).ToArray()), result.HasMore);
    }

    public async Task<ConversationView> LoadAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        return Presentation.Map(await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false));
    }

    public async Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        var conversation = await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false);
        await repository.DeleteAsync(user, conversation.Id, conversation.Revision, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationView> RecordMessageAsync(Guid conversationId, RecordMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireValue(request);
        var conversation = await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false);
        var updated = Access.Validate(() => conversation.RecordMessage(request.SubmissionId, request.Content, timeProvider.GetUtcNow()));
        if (!ReferenceEquals(conversation, updated))
        {
            await repository.SaveAsync(user, updated, conversation.Revision, cancellationToken).ConfigureAwait(false);
        }

        return Presentation.Map(updated);
    }

    public Task<ConversationView> CancelResponseAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
    {
        return RecordResponseAsync(conversationId, messageId, new ResponseDelta(string.Empty, MessageState.Cancelled), cancellationToken);
    }

    /// <summary>Server-only recording of an ordered provider update; never expose this as a browser write endpoint.</summary>
    /// <remarks>Checks current ownership and optimistic revision. Terminal messages reject late updates; deltas are not retried.</remarks>
    public async Task<ConversationView> RecordResponseAsync(Guid conversationId, Guid messageId, ResponseDelta delta,
        CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireValue(delta);
        Access.RequireId(messageId);
        var conversation = await FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false);
        var updated = Access.Validate(() => conversation.UpdateResponse(messageId, delta.Text, delta.State, timeProvider.GetUtcNow(), delta.Failure));
        await repository.SaveAsync(user, updated, conversation.Revision, cancellationToken).ConfigureAwait(false);
        return Presentation.Map(updated);
    }

    /// <summary>Loads an owned conversation and rechecks the returned owner before exposing the snapshot to a use case.</summary>
    /// <remarks>Forwards cancellation to storage. Invalid IDs yield InvalidRequest; absent or foreign data yields NotFound.</remarks>
    private async Task<Conversation> FindAsync(UserId user, Guid conversationId, CancellationToken cancellationToken)
    {
        Access.RequireId(conversationId);
        var conversation = await repository.FindAsync(user, conversationId, cancellationToken).ConfigureAwait(false)
            ?? throw new ChatOperationException(ChatErrorCode.NotFound);
        Access.RequireOwner(user, conversation.OwnerId);
        return conversation;
    }
}
