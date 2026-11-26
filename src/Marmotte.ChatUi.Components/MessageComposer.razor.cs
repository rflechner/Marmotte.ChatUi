using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Marmotte.ChatUi.Components;

/// <summary>Edits a host-owned text draft and awaits acceptance, preventing blank/duplicate submissions and retaining failed drafts.</summary>
public partial class MessageComposer
{
    private readonly string _inputId = $"mcui-input-{Guid.NewGuid():N}";
    private readonly string _helpId = $"mcui-help-{Guid.NewGuid():N}";
    private ElementReference _input;
    private bool _submitting;
    private string? _error;

    /// <summary>Gets or sets the draft. Use two-way binding; the component requests changes without mutating its parameters.</summary>
    [Parameter] public string Draft { get; set; } = string.Empty;

    /// <summary>Gets or sets the awaited callback for edits and clearing after successful acceptance.</summary>
    [Parameter] public EventCallback<string> DraftChanged { get; set; }

    /// <summary>Gets or sets the awaited submission callback. Return after acceptance, not after generation; failures retain the draft.</summary>
    /// <remarks>The host owns cancellation, authorization, submission IDs and generation lifetime. Exception details are hidden.</remarks>
    [Parameter] public EventCallback<string> OnSend { get; set; }

    /// <summary>Gets or sets whether a submission or generation prevents editing and sending.</summary>
    [Parameter] public bool Busy { get; set; }

    /// <summary>Gets or sets a host explanation that blocks sending, for example unsupported pending attachments.</summary>
    [Parameter] public string? BlockedReason { get; set; }

    private bool CanSend => RendererInfo.IsInteractive && OnSend.HasDelegate && DraftChanged.HasDelegate
        && !Busy && !_submitting && string.IsNullOrEmpty(BlockedReason) && !string.IsNullOrWhiteSpace(Draft);

    private string HelpText => BlockedReason ?? "Enter starts a new line. Tab to Send message to submit.";

    private async Task ChangeDraftAsync(ChangeEventArgs args)
    {
        if (Busy || _submitting)
        {
            return;
        }

        _error = null;
        await DraftChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);
    }

    private async Task SubmitAsync()
    {
        if (!CanSend)
        {
            return;
        }

        _submitting = true;
        _error = null;

        try
        {
            await OnSend.InvokeAsync(Draft);
            await DraftChanged.InvokeAsync(string.Empty);
        }
        catch (OperationCanceledException)
        {
            _error = "Sending was cancelled. Your draft is retained.";
        }
        catch (Exception exception)
        {
            _error = exception is ChatOperationException failure
                ? failure.Message
                : "The message could not be sent. Your draft is retained.";
        }
        finally
        {
            _submitting = false;
        }

        if (RendererInfo.IsInteractive)
        {
            await _input.FocusAsync(preventScroll: true);
        }
    }
}
