using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application.Ports;

/// <summary>Unknown capabilities must never be treated as supported.</summary>
public enum CapabilitySupport
{
    Unknown,
    Unsupported,
    Supported
}

/// <summary>Capabilities of this exact profile/model combination, independently verified by an adapter.</summary>
public sealed record ModelCapabilities(CapabilitySupport Streaming, CapabilitySupport ImageInput);

/// <summary>Queries capabilities without assuming that an OpenAI-compatible endpoint supports every API.</summary>
/// <remarks>Server-side only; the caller must authorize the profile. Unknown is a valid result; I/O observes cancellation.</remarks>
public interface IModelCapabilities
{
    Task<ModelCapabilities> GetAsync(ConnectionProfile profile, CancellationToken cancellationToken);
}

/// <summary>A provider-neutral response request; history contains prior context and the new user message, not its pending response.</summary>
/// <remarks>
/// The caller authorizes the profile and history and chooses how to treat earlier partial responses explicitly.
/// Stream defaults to false. Opting into streaming requires verified support from IModelCapabilities.
/// </remarks>
public sealed record ResponseRequest(ConnectionProfile Profile, IReadOnlyList<Message> History, bool Stream = false);

/// <summary>A text delta and lifecycle transition; Failed requires a safe failure category, other states prohibit it.</summary>
public sealed record ResponseDelta(string Text, MessageState State, GenerationFailure? Failure = null);

/// <summary>Produces an ordered, single-consumer response stream without leaking SDK types.</summary>
/// <remarks>
/// Adapters validate options/capabilities before sending, yield Streaming deltas and exactly one terminal update,
/// and never retry a request automatically. Completed may carry final text; Failed/Cancelled preserve prior deltas.
/// Cancellation throws OperationCanceledException, including during enumeration; consumers explicitly persist cancellation.
/// Adapters impose a documented finite timeout, map provider errors to safe failures, and never emit raw exception text.
/// Early iterator disposal must release transport resources. Streaming is opt-in and requires verified support;
/// non-streaming requests yield a single terminal update. Generation orchestration remains a host responsibility.
/// </remarks>
public interface IResponseGenerator
{
    IAsyncEnumerable<ResponseDelta> GenerateAsync(ResponseRequest request, CancellationToken cancellationToken);
}

/// <summary>Discovery outcome; unsupported discovery permits manual model entry and does not forbid chat.</summary>
/// <remarks>Models are opaque identifiers, not capability declarations. Failure never contains provider text.</remarks>
public sealed record ModelDiscoveryResult(CapabilitySupport Support, IReadOnlyList<string> Models, GenerationFailure? Failure = null);

/// <summary>A synthetic connection check; success verifies only the requested text mode for this profile/model.</summary>
public sealed record ModelConnectionResult(bool Succeeded, ModelCapabilities Capabilities, GenerationFailure? Failure = null);

/// <summary>Server-side discovery and explicit synthetic generation probes for an authorized connection profile.</summary>
/// <remarks>
/// CheckAsync sends a small fixed prompt and may consume inference resources; it never uses conversation history.
/// Discovery is optional; a manually entered model need not appear in its result. Cancellation throws
/// OperationCanceledException; provider faults return safe failure categories. Implementations bound both calls in time.
/// </remarks>
public interface IModelConnection
{
    /// <summary>Discovers model identifiers without requiring discovery support for subsequent manual model use.</summary>
    /// <param name="profile">The owned profile with its complete API base URL.</param>
    /// <param name="cancellationToken">Cancels authorization and discovery.</param>
    /// <returns>Support status, identifiers, and an optional safe failure category.</returns>
    Task<ModelDiscoveryResult> DiscoverAsync(ConnectionProfile profile, CancellationToken cancellationToken);

    /// <summary>Explicitly exercises text generation, optionally streaming, without sending conversation history.</summary>
    /// <param name="profile">The owned profile and exact model to verify.</param>
    /// <param name="streaming">Whether to probe streaming instead of ordinary text generation.</param>
    /// <param name="cancellationToken">Cancels authorization and inference.</param>
    /// <returns>Connection success, independently established capabilities, and an optional safe failure.</returns>
    Task<ModelConnectionResult> CheckAsync(ConnectionProfile profile, bool streaming, CancellationToken cancellationToken);
}
