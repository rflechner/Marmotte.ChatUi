namespace Marmotte.ChatUi.Infrastructure.OpenAI;

/// <summary>Immutable transport limits for model operations; contains no endpoint or credential.</summary>
public sealed class OpenAIAdapterOptions
{
    /// <summary>Sets the total deadline, including authorization, headers, and complete response enumeration.</summary>
    /// <param name="operationTimeout">Defaults to two minutes; must be positive and at most thirty minutes.</param>
    /// <exception cref="ArgumentOutOfRangeException">The deadline is outside the finite supported range.</exception>
    public OpenAIAdapterOptions(TimeSpan? operationTimeout = null)
    {
        OperationTimeout = operationTimeout ?? TimeSpan.FromMinutes(2);
        if (OperationTimeout <= TimeSpan.Zero || OperationTimeout > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        }
    }

    /// <summary>Maximum elapsed time for a single operation, including a stalled stream.</summary>
    public TimeSpan OperationTimeout { get; }
}
