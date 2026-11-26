namespace Marmotte.ChatUi.Domain;

/// <summary>Centralizes argument checks shared by domain objects without normalizing the supplied values.</summary>
internal static class Guard
{
    /// <summary>Ensures an entity or submission identifier represents an assigned identity.</summary>
    /// <param name="value">The identifier to validate.</param>
    /// <param name="name">The caller's parameter name, reported when validation fails.</param>
    /// <returns>The unchanged, nonempty identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
    internal static Guid Id(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier must not be empty.", name);
        }

        return value;
    }

    /// <summary>Checks required domain text while preserving its whitespace and casing.</summary>
    /// <param name="value">The text to validate; leading and trailing whitespace are not removed.</param>
    /// <param name="maximumLength">The maximum permitted length, measured in UTF-16 code units.</param>
    /// <param name="name">The caller's parameter name, reported when validation fails.</param>
    /// <returns>The unchanged text after validation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">The text is blank or exceeds <paramref name="maximumLength"/>.</exception>
    internal static string Text(string value, int maximumLength, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"The value exceeds {maximumLength} characters.", name);
        }

        return value;
    }
}
