using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Components;

/// <summary>Displays host-ordered snapshots in a keyboard-scrollable region, without moving focus or announcing every streamed token.</summary>
public partial class MessageList
{
    /// <summary>Gets or sets ordered messages with unique IDs. Replace snapshots when streaming updates arrive.</summary>
    [Parameter] public IReadOnlyList<MessageView> Messages { get; set; } = [];

    /// <summary>Gets or sets the accessible transcript name, useful when several widgets share a page.</summary>
    [Parameter] public string Label { get; set; } = "Conversation messages";

    /// <summary>Gets or sets trusted empty-state content.</summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Gets or sets trusted message content, preserving the surrounding speaker and lifecycle metadata.</summary>
    [Parameter] public RenderFragment<MessageView>? MessageTemplate { get; set; }

    /// <summary>Gets or sets the host-authorized attachment preview.</summary>
    [Parameter] public RenderFragment<AttachmentView>? AttachmentTemplate { get; set; }
}
