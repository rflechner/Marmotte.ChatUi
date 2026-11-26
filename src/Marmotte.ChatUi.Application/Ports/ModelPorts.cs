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
/// <remarks>The caller authorizes the profile and history and chooses how to treat earlier partial responses explicitly.</remarks>
public sealed record ResponseRequest(ConnectionProfile Profile, IReadOnlyList<Message> History);

/// <summary>A text delta and lifecycle transition; Failed requires a safe failure category, other states prohibit it.</summary>
public sealed record ResponseDelta(string Text, MessageState State, GenerationFailure? Failure = null);

/// <summary>Produces an ordered, single-consumer response stream without leaking SDK types.</summary>
/// <remarks>
/// Adapters validate options/capabilities before sending, yield Streaming deltas and exactly one terminal update,
/// and never retry a request automatically. Completed may carry final text; Failed/Cancelled preserve prior deltas.
/// Cancellation throws OperationCanceledException, including during enumeration; consumers explicitly persist cancellation.
/// Adapters impose a documented finite timeout, map provider errors to safe failures, and never emit raw exception text.
/// Early iterator disposal must release transport resources. No concrete provider or generation orchestrator exists in M2.
/// </remarks>
public interface IResponseGenerator
{
    IAsyncEnumerable<ResponseDelta> GenerateAsync(ResponseRequest request, CancellationToken cancellationToken);
}
