using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Components;

/// <summary>Composes a host-controlled chat surface without services, persistence, authentication, or a prescribed render mode.</summary>
public partial class ChatWidget
{
    private bool _cancelling;
    private ChatErrorCode? _cancelError;

    /// <summary>Gets or sets the visible and accessible name.</summary>
    [Parameter] public string Title { get; set; } = "Conversation";

    /// <summary>Gets or sets ordered, immutable message snapshots. The host applies generation deltas once in order.</summary>
    [Parameter] public IReadOnlyList<MessageView> Messages { get; set; } = [];

    /// <summary>Gets or sets pending metadata. This text-only widget blocks sending until pending attachments are removed.</summary>
    [Parameter] public IReadOnlyList<AttachmentView> Attachments { get; set; } = [];

    /// <summary>Gets or sets the host-owned draft; bind with DraftChanged.</summary>
    [Parameter] public string Draft { get; set; } = string.Empty;

    /// <summary>Gets or sets the awaited draft update callback.</summary>
    [Parameter] public EventCallback<string> DraftChanged { get; set; }

    /// <summary>Gets or sets text acceptance; failures retain the draft. The host owns operation cancellation and lifetime.</summary>
    [Parameter] public EventCallback<string> OnSend { get; set; }

    /// <summary>Gets or sets the awaited request to cancel the active response; the host confirms its final status.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>Gets or sets the awaited pending-attachment removal callback.</summary>
    [Parameter] public EventCallback<Guid> OnRemoveAttachment { get; set; }

    /// <summary>Gets or sets the active/latest generation state, independently of historic message states.</summary>
    [Parameter] public GenerationStatus? Status { get; set; }

    /// <summary>Gets or sets whether host acceptance is in progress.</summary>
    [Parameter] public bool Sending { get; set; }

    /// <summary>Gets or sets whether cancellation is awaiting acknowledgement.</summary>
    [Parameter] public bool Cancelling { get; set; }

    /// <summary>Gets or sets a safe error category. Raw exceptions are never rendered.</summary>
    [Parameter] public ChatErrorCode? Error { get; set; }

    /// <summary>Gets or sets the explicit or system-following theme, independently per instance.</summary>
    [Parameter] public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Gets or sets theme selection notification. The host updates Theme and decides whether to persist it.</summary>
    [Parameter] public EventCallback<ThemePreference> ThemeChanged { get; set; }

    /// <summary>Gets or sets whether the native theme selector is displayed.</summary>
    [Parameter] public bool ShowThemeSelector { get; set; } = true;

    /// <summary>Gets or sets optional host classes on the theme container.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Gets or sets trusted instance CSS custom properties; never pass model/user-generated CSS.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Gets or sets host branding. Omitted by default; no mascot is imposed on consumers.</summary>
    [Parameter] public RenderFragment? Branding { get; set; }

    /// <summary>Gets or sets trusted empty-state content.</summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>Gets or sets trusted message content rendering; custom HTML must be sanitized by the host.</summary>
    [Parameter] public RenderFragment<MessageView>? MessageTemplate { get; set; }

    /// <summary>Gets or sets authorized previews, with content ownership and URL cleanup supplied by the host.</summary>
    [Parameter] public RenderFragment<AttachmentView>? AttachmentTemplate { get; set; }

    /// <summary>Gets or sets optional host footer content.</summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    private bool Active => Status is GenerationStatus.Pending or GenerationStatus.Streaming;
    private bool Busy => Sending || Active || Cancelling || _cancelling;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!Active)
        {
            _cancelError = null;
        }
    }

    private Task ChangeThemeAsync(ChangeEventArgs args) => Enum.TryParse<ThemePreference>(args.Value?.ToString(), out var theme)
        && Enum.IsDefined(theme) ? ThemeChanged.InvokeAsync(theme) : Task.CompletedTask;

    private async Task CancelAsync()
    {
        if (!Active || Sending || Cancelling || _cancelling || !OnCancel.HasDelegate)
        {
            return;
        }

        _cancelling = true;
        _cancelError = null;

        try
        {
            await OnCancel.InvokeAsync();
        }
        catch (Exception)
        {
            _cancelError = ChatErrorCode.GenerationFailed;
        }
        finally
        {
            _cancelling = false;
        }
    }
}
