namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Relational conversation header, separate from the immutable domain aggregate.</summary>
internal sealed class ConversationRow
{
    public Guid Id { get; set; }
    public required string OwnerId { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }
}

/// <summary>Relational message state; only a conversation repository transaction may write it.</summary>
internal sealed class MessageRow
{
    public required string OwnerId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public long Sequence { get; set; }
    public int Role { get; set; }
    public required string Content { get; set; }
    public int State { get; set; }
    public int? Failure { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Immutable stored metadata for owned attachments, without file bytes or storage locations.</summary>
internal sealed class AttachmentRow
{
    public Guid Id { get; set; }
    public required string OwnerId { get; set; }
    public required string FileName { get; set; }
    public required string MediaType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int State { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}

/// <summary>Ordered, owner-bound relation between a message and previously prepared attachment metadata.</summary>
internal sealed class MessageAttachmentRow
{
    public required string OwnerId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public Guid AttachmentId { get; set; }
    public int Position { get; set; }
}

/// <summary>Relational connection settings with no plaintext or protected model credentials.</summary>
internal sealed class ConnectionProfileRow
{
    public Guid Id { get; set; }
    public required string OwnerId { get; set; }
    public required string Name { get; set; }
    public required string Endpoint { get; set; }
    public required string Model { get; set; }
    public double? Temperature { get; set; }
    public int? MaxOutputTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One relational preference snapshot per owner, with an optional owned model/profile selection.</summary>
internal sealed class UserPreferencesRow
{
    public required string OwnerId { get; set; }
    public int Theme { get; set; }
    public Guid? SelectedProfileId { get; set; }
    public string? SelectedModel { get; set; }
}

/// <summary>Authenticated ciphertext belonging to one owned profile; plaintext never reaches EF's change tracker.</summary>
internal sealed class ModelCredentialRow
{
    public required string OwnerId { get; set; }
    public Guid ProfileId { get; set; }
    public required byte[] ProtectedValue { get; set; }
}
