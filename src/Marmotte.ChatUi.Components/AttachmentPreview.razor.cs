using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Components;

/// <summary>Displays attachment metadata and preparation state without fetching content or asserting model support.</summary>
public partial class AttachmentPreview
{
    /// <summary>Gets or sets display metadata; the host remains responsible for authorization and format validation.</summary>
    [Parameter, EditorRequired] public AttachmentView Attachment { get; set; } = default!;

    /// <summary>Gets or sets an optional trusted preview. The host authorizes retrieval and owns content/URL lifetimes.</summary>
    [Parameter] public RenderFragment<AttachmentView>? PreviewTemplate { get; set; }

    /// <summary>Gets or sets the awaited removal callback. Omit it for history attachments.</summary>
    [Parameter] public EventCallback<Guid> OnRemove { get; set; }

    /// <summary>Gets or sets whether removal is unavailable while the host is busy.</summary>
    [Parameter] public bool Disabled { get; set; }

    private string StateText => Attachment.Status switch
    {
        AttachmentStatus.Uploaded => "Uploaded — awaiting preparation",
        AttachmentStatus.Preparing => "Preparing attachment…",
        AttachmentStatus.Ready => "Prepared — model support must be verified",
        _ => "Attachment preparation failed"
    };

    private Task RemoveAsync() => Disabled || !OnRemove.HasDelegate
        ? Task.CompletedTask
        : OnRemove.InvokeAsync(Attachment.Id);
}
