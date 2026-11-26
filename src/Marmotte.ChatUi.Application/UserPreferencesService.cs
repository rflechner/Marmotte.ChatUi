using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Authorized user preferences, checking profile ownership before saving. Storage rechecks this atomically.</summary>
public sealed class UserPreferencesService(ICurrentUser currentUser, IUserPreferencesRepository repository,
    IConnectionProfileRepository profiles) : IUserPreferencesService
{
    public async Task<PreferencesView> LoadAsync(CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        var preferences = await repository.FindAsync(user, cancellationToken).ConfigureAwait(false) ?? new UserPreferences(user);
        Access.RequireOwner(user, preferences.OwnerId);
        return Presentation.Map(preferences);
    }

    public async Task<PreferencesView> SaveAsync(PreferencesView preferences, CancellationToken cancellationToken = default)
    {
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Access.RequireValue(preferences);
        var updated = Access.Validate(() => new UserPreferences(user, (ThemeMode)preferences.Theme,
            preferences.SelectedProfileId, preferences.SelectedModel));
        if (updated.SelectedProfileId.HasValue)
        {
            var profile = await profiles.FindAsync(user, updated.SelectedProfileId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new ChatOperationException(ChatErrorCode.NotFound);
            Access.RequireOwner(user, profile.OwnerId);
        }

        await repository.SaveAsync(user, updated, cancellationToken).ConfigureAwait(false);
        return Presentation.Map(updated);
    }
}
