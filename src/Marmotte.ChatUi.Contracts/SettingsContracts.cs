namespace Marmotte.ChatUi.Contracts;

/// <summary>The user's display choice; System follows the consuming host's operating-system preference.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>User preferences; a null model uses the selected profile's model.</summary>
public sealed record PreferencesView(ThemePreference Theme, Guid? SelectedProfileId, string? SelectedModel);

/// <summary>Ordinary connection settings only. Secrets have no representation in this API.</summary>
public sealed record SaveProfileRequest(string Name, Uri Endpoint, string Model, double? Temperature = null, int? MaxOutputTokens = null);

/// <summary>A connection profile's displayable settings and timestamps, excluding credentials and owner identity.</summary>
public sealed record ConnectionProfileView(Guid Id, string Name, Uri Endpoint, string Model,
    double? Temperature, int? MaxOutputTokens, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Current-user preferences. Defaults use System with no selected profile/model.</summary>
/// <remarks>Saving requires ownership of a selected profile. Uses the same error and cancellation contract as IConversationService.</remarks>
public interface IUserPreferencesService
{
    Task<PreferencesView> LoadAsync(CancellationToken cancellationToken = default);
    Task<PreferencesView> SaveAsync(PreferencesView preferences, CancellationToken cancellationToken = default);
}

/// <summary>Current-user connection settings, without model credentials or provider discovery.</summary>
/// <remarks>Uses the same error and cancellation contract as IConversationService. Delete also clears matching preferences.</remarks>
public interface IConnectionProfileService
{
    Task<ConnectionProfileView> CreateAsync(SaveProfileRequest request, CancellationToken cancellationToken = default);
    Task<ConnectionProfileView> UpdateAsync(Guid profileId, SaveProfileRequest request, CancellationToken cancellationToken = default);
    Task<ConnectionProfileView> LoadAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<Page<ConnectionProfileView>> ListAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
