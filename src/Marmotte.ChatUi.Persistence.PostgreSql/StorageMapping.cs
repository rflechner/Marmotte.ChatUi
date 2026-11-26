using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Converts detached relational rows to validated domain snapshots and prepares rows for persistence.</summary>
internal static class StorageMapping
{
    /// <summary>Restores metadata through domain validation; corrupt values fail loading.</summary>
    internal static Attachment ToDomain(this AttachmentRow row) => new(row.Id, new UserId(row.OwnerId), row.FileName,
        row.MediaType, row.SizeBytes, row.CreatedAt, (AttachmentState)row.State, row.Width, row.Height);

    /// <summary>Copies immutable metadata, never file bytes, into an insertable entity.</summary>
    internal static AttachmentRow ToRow(this Attachment attachment) => new()
    {
        Id = attachment.Id,
        OwnerId = attachment.OwnerId.Value,
        FileName = attachment.FileName,
        MediaType = attachment.MediaType,
        SizeBytes = attachment.SizeBytes,
        CreatedAt = attachment.CreatedAt,
        State = (int)attachment.State,
        Width = attachment.Width,
        Height = attachment.Height
    };

    /// <summary>Reconstitutes secret-free connection settings through domain validation.</summary>
    internal static ConnectionProfile ToDomain(this ConnectionProfileRow row) => new(row.Id, new UserId(row.OwnerId), row.Name,
        new Uri(row.Endpoint), row.Model, new GenerationOptions(row.Temperature, row.MaxOutputTokens), row.CreatedAt, row.UpdatedAt);

    /// <summary>Copies validated settings without introducing secrets into the tracked entity.</summary>
    internal static ConnectionProfileRow ToRow(this ConnectionProfile profile) => new()
    {
        Id = profile.Id,
        OwnerId = profile.OwnerId.Value,
        Name = profile.Name,
        Endpoint = profile.Endpoint.AbsoluteUri,
        Model = profile.Model,
        Temperature = profile.Options.Temperature,
        MaxOutputTokens = profile.Options.MaxOutputTokens,
        CreatedAt = profile.CreatedAt,
        UpdatedAt = profile.UpdatedAt
    };

    /// <summary>Restores validated theme and selection settings for the stored owner.</summary>
    internal static UserPreferences ToDomain(this UserPreferencesRow row) => new(new UserId(row.OwnerId),
        (ThemeMode)row.Theme, row.SelectedProfileId, row.SelectedModel);
}
