using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Components;

/// <summary>Announces lifecycle transitions and safe error categories separately from streamed message text.</summary>
public partial class GenerationIndicator
{
    /// <summary>Gets or sets the current response state; null means no generation yet.</summary>
    [Parameter] public GenerationStatus? Status { get; set; }

    /// <summary>Gets or sets whether submission is being accepted by the host.</summary>
    [Parameter] public bool Sending { get; set; }

    /// <summary>Gets or sets whether cancellation is awaiting host acknowledgement.</summary>
    [Parameter] public bool Cancelling { get; set; }

    /// <summary>Gets or sets a safe failure category; provider exception messages are never rendered.</summary>
    [Parameter] public ChatErrorCode? Error { get; set; }

    private string StateName => Cancelling ? "cancelling" : Sending ? "sending" : Status?.ToString().ToLowerInvariant() ?? "idle";

    private string StatusText => Cancelling ? "Stopping response…" : Sending ? "Sending message…" : Status switch
    {
        GenerationStatus.Pending => "Waiting for a response…",
        GenerationStatus.Streaming => "Response in progress…",
        GenerationStatus.Completed => "Response complete.",
        GenerationStatus.Cancelled => "Response stopped. Partial response retained.",
        GenerationStatus.Failed => "Response failed. Any partial response is retained.",
        _ => "Ready for your message."
    };

    private string ErrorText => new ChatOperationException(Error!.Value).Message;
}
