using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Maps domain snapshots to presentation data without exposing ownership or storage details.</summary>
/// <remarks>Callers must authorize snapshots before mapping; these helpers do not perform access checks.</remarks>
internal static class Presentation
{
    /// <summary>Copies a complete ordered history into detached, read-only presentation collections.</summary>
    internal static ConversationView Map(Conversation conversation)
    {
        return new ConversationView(new ConversationSummary(conversation.Id, conversation.Title, conversation.CreatedAt,
            conversation.UpdatedAt, conversation.Revision), Array.AsReadOnly(conversation.Messages.Select(Map).ToArray()));
    }

    /// <summary>Preserves partial content and attachment metadata while reducing failure details to a safe UI category.</summary>
    private static MessageView Map(Message message)
    {
        return new MessageView(message.Id, message.SubmissionId, message.Sequence, (ChatRole)message.Role, message.Content,
            (GenerationStatus)message.State, message.CreatedAt, message.UpdatedAt,
            Array.AsReadOnly(message.Attachments.Select(attachment => new AttachmentView(attachment.Id, attachment.FileName,
                attachment.MediaType, attachment.SizeBytes, (AttachmentStatus)attachment.State, attachment.Width, attachment.Height)).ToArray()),
            message.Failure.HasValue ? ModelConnectionService.ErrorCode(message.Failure.Value) : null);
    }

    /// <summary>Projects ordinary connection settings and timestamps without the profile's owner identity.</summary>
    internal static ConnectionProfileView Map(ConnectionProfile profile)
    {
        return new ConnectionProfileView(profile.Id, profile.Name, profile.Endpoint, profile.Model,
            profile.Options.Temperature, profile.Options.MaxOutputTokens, profile.CreatedAt, profile.UpdatedAt);
    }

    /// <summary>Projects the user's display and model selections without their owner identity.</summary>
    internal static PreferencesView Map(UserPreferences preferences)
    {
        return new PreferencesView((ThemePreference)preferences.Theme, preferences.SelectedProfileId, preferences.SelectedModel);
    }
}
