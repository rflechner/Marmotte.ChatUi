using Marmotte.ChatUi.Application.Ports;

namespace Marmotte.ChatUi.Application;

/// <summary>Authorizes credential replacement, removal, and server-only retrieval from the current identity.</summary>
/// <remarks>Scoped to the host user. Never expose ResolveAsync through a browser endpoint or component contract.</remarks>
public sealed class ModelCredentialService(ICurrentUser currentUser, IModelCredentialStore store)
{
    /// <summary>Resolves an owned profile's credential for a server-side model call; null means no credential.</summary>
    /// <remarks>Unauthenticated and NotFound use safe application errors; cancellation propagates to storage.</remarks>
    public async Task<ModelCredential?> ResolveAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireId(profileId);
        return await store.FindAsync(user, profileId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates and replaces an owned profile's secret without returning it; cancellation propagates.</summary>
    /// <remarks>Invalid credentials yield InvalidRequest; absent/foreign profiles yield NotFound.</remarks>
    public async Task SaveAsync(Guid profileId, string value, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireId(profileId);
        var credential = Access.Validate(() => new ModelCredential(value));
        await store.SaveAsync(user, profileId, credential, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes an owned profile's credential; absent/foreign profiles yield NotFound and cancellation propagates.</summary>
    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireId(profileId);
        await store.DeleteAsync(user, profileId, cancellationToken).ConfigureAwait(false);
    }
}
