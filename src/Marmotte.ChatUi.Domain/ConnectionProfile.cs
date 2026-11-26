namespace Marmotte.ChatUi.Domain;

/// <summary>Optional model parameters. Server-specific support and ranges must be checked by the adapter.</summary>
public sealed class GenerationOptions
{
    /// <summary>Rejects non-finite/negative temperature and non-positive output limits; null uses server defaults.</summary>
    public GenerationOptions(double? temperature = null, int? maxOutputTokens = null)
    {
        if (temperature.HasValue && (!double.IsFinite(temperature.Value) || temperature.Value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature));
        }

        if (maxOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));
        }

        Temperature = temperature;
        MaxOutputTokens = maxOutputTokens;
    }

    public double? Temperature { get; }
    public int? MaxOutputTokens { get; }
}

/// <summary>An immutable user-owned model connection. Credentials are deliberately absent.</summary>
public sealed class ConnectionProfile
{
    /// <summary>Creates validated settings without assuming any API path, port, capabilities, or authentication requirement.</summary>
    /// <remarks>HTTP(S) is allowed for local servers. User info, queries, and fragments are rejected to keep secrets out of settings.</remarks>
    public ConnectionProfile(Guid id, UserId ownerId, string name, Uri endpoint, string model,
        GenerationOptions options, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = Guard.Id(id, nameof(id));
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(options);
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(endpoint.Host) || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException("Use an absolute HTTP(S) endpoint without credentials, query, or fragment.", nameof(endpoint));
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("Profile time cannot move backwards.", nameof(updatedAt));
        }

        OwnerId = ownerId;
        Name = Guard.Text(name, 100, nameof(name));
        Endpoint = endpoint;
        Model = Guard.Text(model, 256, nameof(model));
        Options = options;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public Guid Id { get; }
    public UserId OwnerId { get; }
    public string Name { get; }
    public Uri Endpoint { get; }
    public string Model { get; }
    public GenerationOptions Options { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
}
