using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Authorized, secret-free profile management. Compose with host-scoped identity and storage adapters.</summary>
public sealed class ConnectionProfileService(ICurrentUser currentUser, IConnectionProfileRepository repository, TimeProvider timeProvider)
    : IConnectionProfileService
{
    public async Task<ConnectionProfileView> CreateAsync(SaveProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var profile = CreateProfile(Guid.NewGuid(), user, request, now, now);
        await repository.SaveAsync(user, profile, true, cancellationToken).ConfigureAwait(false);
        return Presentation.Map(profile);
    }

    public async Task<ConnectionProfileView> UpdateAsync(Guid profileId, SaveProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        var previous = await FindAsync(user, profileId, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        if (now < previous.UpdatedAt)
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }

        var updated = CreateProfile(profileId, user, request, previous.CreatedAt, now);
        await repository.SaveAsync(user, updated, false, cancellationToken).ConfigureAwait(false);
        return Presentation.Map(updated);
    }

    public async Task<ConnectionProfileView> LoadAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        return Presentation.Map(await FindAsync(user, profileId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Page<ConnectionProfileView>> ListAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireValue(page);
        var result = await repository.ListAsync(user, page, cancellationToken).ConfigureAwait(false);
        foreach (var profile in result.Items)
        {
            Access.RequireOwner(user, profile.OwnerId);
        }

        return new Page<ConnectionProfileView>(Array.AsReadOnly(result.Items.Select(Presentation.Map).ToArray()), result.HasMore);
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        await FindAsync(user, profileId, cancellationToken).ConfigureAwait(false);
        await repository.DeleteAsync(user, profileId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads an owned profile and rechecks the returned owner before exposing its settings to a use case.</summary>
    /// <remarks>Forwards cancellation to storage. Invalid IDs yield InvalidRequest; absent or foreign data yields NotFound.</remarks>
    private async Task<ConnectionProfile> FindAsync(UserId user, Guid profileId, CancellationToken cancellationToken)
    {
        Access.RequireId(profileId);
        var profile = await repository.FindAsync(user, profileId, cancellationToken).ConfigureAwait(false)
            ?? throw new ChatOperationException(ChatErrorCode.NotFound);
        Access.RequireOwner(user, profile.OwnerId);
        return profile;
    }

    /// <summary>Builds validated profile settings without persisting them or resolving credentials.</summary>
    /// <remarks>The ID, owner, and timestamps come from the service; invalid request values yield InvalidRequest.</remarks>
    private static ConnectionProfile CreateProfile(Guid id, UserId user, SaveProfileRequest request,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Access.RequireValue(request);
        return Access.Validate(() => new ConnectionProfile(id, user, request.Name, request.Endpoint, request.Model,
            new GenerationOptions(request.Temperature, request.MaxOutputTokens), createdAt, updatedAt));
    }
}
