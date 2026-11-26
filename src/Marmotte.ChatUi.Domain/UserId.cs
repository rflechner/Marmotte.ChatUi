namespace Marmotte.ChatUi.Domain;

/// <summary>A stable, opaque application identity mapped by the host from its trusted identity provider.</summary>
public sealed record UserId
{
    /// <summary>Creates an identity; blank values and values longer than 256 characters are rejected.</summary>
    public UserId(string value)
    {
        Value = Guard.Text(value, 256, nameof(value));
    }

    /// <summary>Gets the case-sensitive application identifier, never an unverified browser owner field.</summary>
    public string Value { get; }
}
