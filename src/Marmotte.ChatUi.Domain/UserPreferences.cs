namespace Marmotte.ChatUi.Domain;

/// <summary>The user's theme choice; System follows the consuming host's operating-system preference.</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>Immutable settings belonging to one user. The application validates ownership of the selected profile.</summary>
public sealed class UserPreferences
{
    /// <summary>Creates preferences; a model override requires a selected profile. Null selections use host defaults.</summary>
    public UserPreferences(UserId ownerId, ThemeMode theme = ThemeMode.System, Guid? selectedProfileId = null,
        string? selectedModel = null)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme));
        }

        if (selectedProfileId.HasValue)
        {
            Guard.Id(selectedProfileId.Value, nameof(selectedProfileId));
        }

        if (selectedModel is not null)
        {
            Guard.Text(selectedModel, 256, nameof(selectedModel));
            if (selectedProfileId is null)
            {
                throw new ArgumentException("A selected model requires a selected profile.", nameof(selectedModel));
            }
        }

        OwnerId = ownerId;
        Theme = theme;
        SelectedProfileId = selectedProfileId;
        SelectedModel = selectedModel;
    }

    public UserId OwnerId { get; }
    public ThemeMode Theme { get; }
    public Guid? SelectedProfileId { get; }
    public string? SelectedModel { get; }
}
