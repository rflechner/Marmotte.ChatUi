using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application.Ports;

/// <summary>Server-only credential storage, separate from profile settings and presentation contracts.</summary>
/// <remarks>
/// All operations require a trusted owner and an existing owned profile (NotFound otherwise).
/// Find returns null when the profile needs no credential. Save replaces the credential; Delete is idempotent
/// for an existing profile. Profile deletion removes its credential atomically. No automatic retries.
/// Implementations protect values at rest and never log them. All I/O propagates cancellation.
/// </remarks>
public interface IModelCredentialStore
{
    Task<ModelCredential?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken);
    Task SaveAsync(UserId ownerId, Guid profileId, ModelCredential credential, CancellationToken cancellationToken);
    Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken);
}

/// <summary>A validated server-only secret with redacted diagnostic formatting; never serialize or log Value.</summary>
public sealed class ModelCredential
{
    /// <summary>Accepts a nonblank credential of at most 8192 characters without control characters.</summary>
    /// <exception cref="ArgumentException">The secret is empty, too long, or contains control characters.</exception>
    public ModelCredential(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid model credential.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets plaintext for server transport only; managed strings cannot be reliably erased from memory.</summary>
    public string Value { get; }

    /// <summary>Returns a fixed marker, never the secret.</summary>
    public override string ToString() => "[REDACTED]";
}
