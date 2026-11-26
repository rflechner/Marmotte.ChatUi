using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Web.Components;

/// <summary>Owns the authenticated workspace's presentation state and observes one generation until completion or disposal.</summary>
public partial class ChatWorkspace : IAsyncDisposable
{
    private const int PageSize = 20;
    private readonly CancellationTokenSource _lifetime = new();
    private PreferencesView _preferences = new(ThemePreference.System, null, null);
    private readonly List<ConnectionProfileView> _profiles = [];
    private IReadOnlyList<ConversationSummary> _history = [];
    private ConversationView? _conversation;
    private Task _generation = Task.CompletedTask;
    private string _draft = "";
    private string _title = "New conversation";
    private string? _error;
    private RecordMessageRequest? _submission;
    private int _offset;
    private int _profileOffset;
    private bool _hasMore;
    private bool _moreProfiles;
    private bool _loaded;
    private bool _busy;
    private bool _accepting;
    private bool _settingsBusy;
    private bool _running;
    private bool _stream;
    private bool _confirmDelete;
    private bool _disposed;

    [Inject] private IConversationService Conversations { get; set; } = default!;
    [Inject] private IConnectionProfileService Profiles { get; set; } = default!;
    [Inject] private IUserPreferencesService Preferences { get; set; } = default!;
    [Inject] private ChatService Chat { get; set; } = default!;

    private ConnectionProfileView? SelectedProfile => _profiles.Find(profile => profile.Id == _preferences.SelectedProfileId);
    private MessageView? LastMessage => _conversation is { Messages.Count: > 0 } ? _conversation.Messages[^1] : null;
    private GenerationStatus? Status => LastMessage?.Status;
    private bool Active => Status is GenerationStatus.Pending or GenerationStatus.Streaming;
    private bool Locked => !_loaded || _busy || _settingsBusy || _running || Active;
    private string? BlockedReason => !_loaded ? "Wait for the workspace to load."
        : _busy ? "Wait for the workspace operation to finish."
        : _settingsBusy ? "Wait for the connection operation to finish."
        : _running && !Active ? "Finishing the response…"
        : _conversation is null ? "Create or open a conversation first."
        : SelectedProfile is null ? "Save and choose a connection profile first." : null;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RunAsync(async () =>
            {
                _preferences = await Preferences.LoadAsync(_lifetime.Token);
                await LoadProfilesAsync();
                if (_preferences.SelectedProfileId is { } id && SelectedProfile is null)
                {
                    _profiles.Add(await Profiles.LoadAsync(id, _lifetime.Token));
                }

                await LoadHistoryAsync();
                _loaded = true;
            });
            StateHasChanged();
        }
    }

    private async Task LoadHistoryAsync()
    {
        var page = await Conversations.ListAsync(new PageRequest(_offset, PageSize), _lifetime.Token);
        _history = page.Items;
        _hasMore = page.HasMore;
    }

    private async Task LoadProfilesAsync()
    {
        var page = await Profiles.ListAsync(new PageRequest(0, PageSize), _lifetime.Token);
        _profiles.Clear();
        _profiles.AddRange(page.Items);
        _profileOffset = page.Items.Count;
        _moreProfiles = page.HasMore;
    }

    private Task MoreProfilesAsync() => RunAsync(async () =>
    {
        var page = await Profiles.ListAsync(new PageRequest(_profileOffset, PageSize), _lifetime.Token);
        _profiles.AddRange(page.Items.Where(profile => _profiles.All(existing => existing.Id != profile.Id)));
        _profileOffset += page.Items.Count;
        _moreProfiles = page.HasMore;
    });

    private Task PreviousAsync() => RunAsync(async () =>
    {
        _offset = Math.Max(0, _offset - PageSize);
        await LoadHistoryAsync();
    });
    private Task NextAsync() => RunAsync(async () =>
    {
        _offset += PageSize;
        await LoadHistoryAsync();
    });

    private Task CreateAsync() => RunAsync(async () =>
    {
        _conversation = await Conversations.CreateAsync(_title, _lifetime.Token);
        ResetDraft();
        await LoadHistoryAsync();
    });

    private Task OpenAsync(Guid id) => RunAsync(async () =>
    {
        _conversation = await Chat.LoadAsync(id, _lifetime.Token);
        ResetDraft();
    });

    private Task ReloadAsync() => RunAsync(async () =>
    {
        if (_conversation is not null)
        {
            _conversation = await Chat.LoadAsync(_conversation.Summary.Id, _lifetime.Token);
        }
    });

    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (_conversation is not null)
        {
            await Chat.DeleteAsync(_conversation.Summary.Id, _lifetime.Token);
            _conversation = null;
            ResetDraft();
            _offset = 0;
            await LoadHistoryAsync();
        }
    });

    private void ResetDraft()
    {
        _draft = "";
        _submission = null;
        _confirmDelete = false;
    }

    private Task ChangeDraftAsync(string value)
    {
        _draft = value;
        return Task.CompletedTask;
    }

    private async Task SendAsync(string text)
    {
        if (Locked || _conversation is null || SelectedProfile is null)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        _busy = true;
        _accepting = true;
        _error = null;
        // Preserve this ID after an ambiguous acceptance failure; a retry of the same draft cannot duplicate inference.
        if (_submission?.Content != text)
        {
            _submission = new(Guid.NewGuid(), text);
        }

        var updates = Chat.SendAsync(_conversation.Summary.Id, SelectedProfile.Id, _submission, _stream, _lifetime.Token)
            .GetAsyncEnumerator(_lifetime.Token);
        try
        {
            if (await updates.MoveNextAsync())
            {
                _conversation = updates.Current;
            }

            _submission = null;
            _running = true;
            _generation = ContinueAsync(updates);
        }
        catch
        {
            await updates.DisposeAsync();
            throw;
        }
        finally
        {
            _accepting = false;
            _busy = false;
        }
    }

    /// <summary>Observes the owned iterator, renders previews, and reloads durable state after any uncertain failure.</summary>
    private async Task ContinueAsync(IAsyncEnumerator<ConversationView> updates)
    {
        await Task.Yield();
        try
        {
            await using (updates)
            {
                while (await updates.MoveNextAsync())
                {
                    _conversation = updates.Current;
                    if (!_disposed)
                    {
                        await InvokeAsync(StateHasChanged);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _error = SafeError(exception);
            if (!_disposed && _conversation is not null)
            {
                try
                {
                    _conversation = await Chat.LoadAsync(_conversation.Summary.Id, _lifetime.Token);
                }
                catch (Exception)
                {
                    _error = "The saved state could not be reloaded. Reload the page before sending again.";
                }
            }
        }
        finally
        {
            _running = false;
            if (!_disposed)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task CancelAsync()
    {
        if (_conversation is not null)
        {
            await Chat.CancelAsync(_conversation.Summary.Id, _lifetime.Token);
            await _generation;
            _conversation = await Chat.LoadAsync(_conversation.Summary.Id, _lifetime.Token);
        }
    }

    private Task SelectProfileAsync(ChangeEventArgs args) => RunAsync(async () =>
    {
        var id = Guid.TryParse(args.Value?.ToString(), out var parsed) ? parsed : (Guid?)null;
        _preferences = await Preferences.SaveAsync(_preferences with { SelectedProfileId = id, SelectedModel = null }, _lifetime.Token);
        _stream = false;
    });

    private Task ChangeThemeAsync(ThemePreference theme) => RunAsync(async () =>
        _preferences = await Preferences.SaveAsync(_preferences with { Theme = theme }, _lifetime.Token));

    private Task ProfileSavedAsync(Guid id) => RunAsync(async () =>
    {
        await LoadProfilesAsync();
        if (_profiles.All(profile => profile.Id != id))
        {
            _profiles.Add(await Profiles.LoadAsync(id, _lifetime.Token));
        }
        _preferences = await Preferences.SaveAsync(_preferences with { SelectedProfileId = id, SelectedModel = null }, _lifetime.Token);
        _stream = false;
    });

    private Task ProfileDeletedAsync() => RunAsync(async () =>
    {
        _preferences = await Preferences.LoadAsync(_lifetime.Token);
        await LoadProfilesAsync();
        _stream = false;
    });

    private Task StreamingVerifiedAsync(bool verified)
    {
        _stream = verified;
        return Task.CompletedTask;
    }
    private Task SettingsBusyAsync(bool busy)
    {
        _settingsBusy = busy;
        return Task.CompletedTask;
    }

    /// <summary>Contains UI action failures without rendering raw adapter exceptions or terminating the circuit.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        _error = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _error = SafeError(exception);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Returns actionable application text or a generic message without exposing adapter exception details.</summary>
    internal static string SafeError(Exception exception) => exception is ChatOperationException safe
        ? safe.Message : exception is OperationCanceledException ? "The operation was cancelled. Reload to check saved state."
        : "The operation could not finish. Reload to check saved state before retrying.";

    /// <summary>Cancels inference and observes its cleanup before releasing this component's lifetime token.</summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _lifetime.CancelAsync();
        await _generation;
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
