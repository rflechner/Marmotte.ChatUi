using Marmotte.ChatUi.Contracts;

namespace Marmotte.ChatUi.Sample.Components.Pages;

/// <summary>Owns transient demo state and simulated generation lifetime, demonstrating explicit widget inputs and callbacks.</summary>
public partial class Home
{
    private static readonly IReadOnlyList<AttachmentView> PreviewAttachments = Enum.GetValues<AttachmentStatus>()
        .Select(state => new AttachmentView(Guid.NewGuid(), $"field-notes-{state.ToString().ToLowerInvariant()}.png", "image/png", 24800, state, 320, 240)).ToArray();
    private readonly Guid _conversationId = Guid.NewGuid();
    private MessageView[] _messages = [];
    private AttachmentView[] _attachments = [];
    private string _draft = string.Empty;
    private string _scenario = "complete";
    private ThemePreference _theme;
    private GenerationStatus? _status;
    private ChatErrorCode? _error;
    private bool _sending;
    private bool _cancelling;
    private bool _disposed;
    private CancellationTokenSource? _generationCancellation;
    private Task _generation = Task.CompletedTask;

    private bool IsBusy => _sending || _cancelling || _status is GenerationStatus.Pending or GenerationStatus.Streaming;

    private async Task SendAsync(string content)
    {
        if (IsBusy || _attachments.Length > 0)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        _sending = true;
        _error = null;
        try
        {
            await Task.Delay(300);
            if (_disposed)
            {
                throw new OperationCanceledException();
            }

            if (_scenario == "send-error")
            {
                throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
            }

            var now = DateTimeOffset.UtcNow;
            var submissionId = Guid.NewGuid();
            var responseId = Guid.NewGuid();
            _messages = [.. _messages,
                new MessageView(Guid.NewGuid(), submissionId, _messages.Length, ChatRole.User, content, GenerationStatus.Completed, now, now, [], null),
                new MessageView(responseId, submissionId, _messages.Length + 1, ChatRole.Assistant, string.Empty, GenerationStatus.Pending, now, now, [], null)];
            _status = GenerationStatus.Pending;
            _generationCancellation?.Dispose();
            _generationCancellation = new CancellationTokenSource();
            _generation = GenerateAsync(responseId, _scenario == "partial-error", _generationCancellation.Token);
        }
        finally
        {
            _sending = false;
            // Blazor does not rerender the callback receiver after an exception; release its busy UI explicitly.
            if (!_disposed)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task GenerateAsync(Guid responseId, bool fail, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in Responder.StreamAsync(_conversationId, responseId, fail, cancellationToken))
            {
                _status = update.Status;
                _error = update.Error;
                UpdateResponse(responseId, update.TextDelta, update.Status, update.Error);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _status = GenerationStatus.Cancelled;
            UpdateResponse(responseId, string.Empty, GenerationStatus.Cancelled, null);
        }
        finally
        {
            if (!_disposed)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private void UpdateResponse(Guid responseId, string delta, GenerationStatus status, ChatErrorCode? error)
    {
        _messages = _messages.Select(message => message.Id == responseId
            ? message with { Content = message.Content + delta, Status = status, Error = error, UpdatedAt = DateTimeOffset.UtcNow }
            : message).ToArray();
    }

    private async Task CancelAsync()
    {
        _cancelling = true;
        try
        {
            if (_generationCancellation is not null)
            {
                await _generationCancellation.CancelAsync();
            }

            await _generation;
        }
        finally
        {
            _cancelling = false;
        }
    }

    private void Reset()
    {
        if (IsBusy)
        {
            return;
        }

        _messages = [];
        _status = null;
        _error = null;
    }

    private void AddAttachment(AttachmentStatus state)
    {
        if (!IsBusy)
        {
            _attachments = [.. _attachments, PreviewAttachments.Single(attachment => attachment.Status == state) with { Id = Guid.NewGuid() }];
        }
    }

    private void RemoveAttachment(Guid id) => _attachments = _attachments.Where(attachment => attachment.Id != id).ToArray();

    /// <summary>Cancels and awaits simulated work when navigating away; no response continues after the demo is disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_generationCancellation is not null)
        {
            await _generationCancellation.CancelAsync();
            await _generation;
            _generationCancellation.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
