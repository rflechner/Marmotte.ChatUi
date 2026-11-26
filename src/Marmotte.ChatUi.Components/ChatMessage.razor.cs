using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Components;

/// <summary>Renders a detached message as encoded plain text, retaining partial responses and attachment metadata.</summary>
public partial class ChatMessage
{
    /// <summary>Gets or sets the message snapshot. Dates are displayed in UTC; input order belongs to the host.</summary>
    [Parameter, EditorRequired] public MessageView Message { get; set; } = default!;

    /// <summary>Gets or sets trusted replacement content. Custom HTML/Markdown renderers must sanitize untrusted content.</summary>
    [Parameter] public RenderFragment<MessageView>? ContentTemplate { get; set; }

    /// <summary>Gets or sets an authorized attachment preview template; no content is fetched by this component.</summary>
    [Parameter] public RenderFragment<AttachmentView>? AttachmentTemplate { get; set; }

    private string Speaker => Message.Role == ChatRole.User ? "You" : "Assistant";

    private string MessageState => Message.Status switch
    {
        GenerationStatus.Pending => "Waiting for a response…",
        GenerationStatus.Streaming => "Responding…",
        GenerationStatus.Cancelled => "Stopped — partial response retained.",
        _ => "Response failed — any partial response is retained."
    };
}
