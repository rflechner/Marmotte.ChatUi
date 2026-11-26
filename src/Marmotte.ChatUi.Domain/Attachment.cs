namespace Marmotte.ChatUi.Domain;

/// <summary>Preparation status only; metadata does not establish that bytes have been validated.</summary>
public enum AttachmentState
{
    Uploaded,
    Preparing,
    Ready,
    Failed
}

/// <summary>Owned attachment metadata. Contains neither binary content nor a storage path or public URL.</summary>
public sealed class Attachment
{
    /// <summary>Creates metadata; rejects empty identifiers, invalid names, sizes, states, and dimensions.</summary>
    /// <remarks>Actual file validation and authorized binary access belong to the future upload adapter.</remarks>
    public Attachment(Guid id, UserId ownerId, string fileName, string mediaType, long sizeBytes,
        DateTimeOffset createdAt, AttachmentState state, int? width = null, int? height = null)
    {
        Id = Guard.Id(id, nameof(id));
        ArgumentNullException.ThrowIfNull(ownerId);
        OwnerId = ownerId;
        FileName = Guard.Text(fileName, 255, nameof(fileName));
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Any(char.IsControl))
        {
            throw new ArgumentException("Use a file name without directory or control characters.", nameof(fileName));
        }

        MediaType = Guard.Text(mediaType, 127, nameof(mediaType));
        if (!mediaType.Contains('/') || mediaType.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A media type is required.", nameof(mediaType));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        if (!Enum.IsDefined(state) || width is <= 0 || height is <= 0 || width.HasValue != height.HasValue)
        {
            throw new ArgumentException("Invalid attachment state or dimensions.");
        }

        SizeBytes = sizeBytes;
        CreatedAt = createdAt.ToUniversalTime();
        State = state;
        Width = width;
        Height = height;
    }

    public Guid Id { get; }
    public UserId OwnerId { get; }
    public string FileName { get; }
    public string MediaType { get; }
    public long SizeBytes { get; }
    public DateTimeOffset CreatedAt { get; }
    public AttachmentState State { get; }
    public int? Width { get; }
    public int? Height { get; }
}
