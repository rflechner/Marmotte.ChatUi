using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Web.Components;

/// <summary>Edits owned connection settings, accepts write-only credentials, and presents explicit model probes.</summary>
public partial class ConnectionSettings : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private ConnectionProfileView? _previous;
    private Guid? _id;
    private string _name = "", _endpoint = "", _model = "", _key = "", _credentialAction = "keep";
    private double? _temperature;
    private int? _maxTokens;
    private IReadOnlyList<string> _models = [];
    private string? _error, _notice;
    private bool _busy, _confirmDelete;

    [Inject] private IConnectionProfileService Profiles { get; set; } = default!;
    [Inject] private ModelCredentialService Credentials { get; set; } = default!;
    [Inject] private ModelConnectionService Connections { get; set; } = default!;

    /// <summary>Gets or sets the owned profile selected by the workspace, without its credential.</summary>
    [Parameter] public ConnectionProfileView? Profile { get; set; }
    /// <summary>Gets or sets whether another workspace operation prevents edits.</summary>
    [Parameter] public bool Disabled { get; set; }
    /// <summary>Gets or sets notification after profile persistence; the host refreshes and selects it.</summary>
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    /// <summary>Gets or sets notification after deleting settings and their protected credential.</summary>
    [Parameter] public EventCallback OnDeleted { get; set; }
    /// <summary>Gets or sets notification when the saved model's streaming probe succeeds or fails.</summary>
    [Parameter] public EventCallback<bool> OnStreamingVerified { get; set; }
    /// <summary>Gets or sets notification to block conflicting workspace operations during I/O.</summary>
    [Parameter] public EventCallback<bool> BusyChanged { get; set; }

    private bool Unavailable => Disabled || _busy || !RendererInfo.IsInteractive;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (_previous != Profile)
        {
            _previous = Profile;
            // The parent can render a saved snapshot after the save callback has returned. Keep that operation's feedback.
            if (_id != Profile?.Id)
            {
                Edit(Profile);
            }
        }
    }

    private void NewProfile() => Edit(null);

    private void Edit(ConnectionProfileView? profile)
    {
        _id = profile?.Id;
        _name = profile?.Name ?? "";
        _endpoint = profile?.Endpoint.AbsoluteUri ?? "";
        _model = profile?.Model ?? "";
        _temperature = profile?.Temperature;
        _maxTokens = profile?.MaxOutputTokens;
        _key = "";
        _credentialAction = "keep";
        _models = [];
        _confirmDelete = false;
        _error = _notice = null;
    }

    private Task SaveAsync() => RunAsync(async token =>
    {
        if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var endpoint) || _temperature is < 0 or > 2
            || (_temperature.HasValue && !double.IsFinite(_temperature.Value)) || _maxTokens is <= 0 || _model.Any(char.IsControl))
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }

        var credentialAction = _credentialAction;
        var key = _key;
        _key = "";
        if (credentialAction == "replace" && (string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Any(char.IsControl)))
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }

        var request = new SaveProfileRequest(_name, endpoint, _model, _temperature, _maxTokens);
        var profile = _id is { } id ? await Profiles.UpdateAsync(id, request, token) : await Profiles.CreateAsync(request, token);
        _id = profile.Id;
        _notice = "Profile settings saved.";
        await OnSaved.InvokeAsync(profile.Id);
        if (credentialAction == "replace")
        {
            await Credentials.SaveAsync(profile.Id, key, token);
        }
        if (credentialAction == "remove")
        {
            await Credentials.DeleteAsync(profile.Id, token);
        }
        _credentialAction = "keep";
        _notice = "Connection saved. You can now test it or send a message.";
    });

    private Task DiscoverAsync() => RunAsync(async token =>
    {
        var result = await Connections.DiscoverAsync(_id!.Value, token);
        _models = result.Models;
        _notice = result.Support == CapabilitySupport.Unsupported
            ? "Discovery is not supported. Enter the model name manually and save."
            : $"Found {_models.Count} models. Choose one in the Model field and save.";
        if (result.Failure is { } failure)
        {
            throw new ChatOperationException(ModelConnectionService.ErrorCode(failure));
        }
    });

    private Task CheckAsync(bool streaming) => RunAsync(async token =>
    {
        var result = await Connections.CheckAsync(_id!.Value, streaming, token);
        if (streaming)
        {
            await OnStreamingVerified.InvokeAsync(result.Succeeded);
        }
        if (!result.Succeeded)
        {
            throw new ChatOperationException(result.Failure is { } failure ? ModelConnectionService.ErrorCode(failure) : ChatErrorCode.GenerationFailed);
        }

        _notice = streaming ? "Streaming verified. Stream response is enabled for this connection." : "Text connection verified.";
    });

    private Task DeleteAsync() => RunAsync(async token =>
    {
        await Profiles.DeleteAsync(_id!.Value, token);
        Edit(null);
        await OnDeleted.InvokeAsync();
        _notice = "Connection and saved key deleted.";
    });

    private Task CancelOperationAsync() => _operation?.CancelAsync() ?? Task.CompletedTask;

    /// <summary>Owns one cancellable settings operation and renders only safe failure messages.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (Unavailable)
        {
            return;
        }
        _busy = true;
        _error = _notice = null;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        await BusyChanged.InvokeAsync(true);
        try
        {
            await action(operation.Token);
        }
        catch (Exception exception)
        {
            _error = ChatWorkspace.SafeError(exception);
        }
        finally
        {
            _key = "";
            _operation = null;
            _busy = false;
            if (!_lifetime.IsCancellationRequested)
            {
                await BusyChanged.InvokeAsync(false);
            }
        }
    }

    /// <summary>Cancels pending model probes and clears unsaved credential input when the workspace closes.</summary>
    public void Dispose()
    {
        _key = "";
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
