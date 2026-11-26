using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Core.Tests;

/// <summary>Provides a mutable trusted identity so tests can simulate user switches and expired sessions between calls.</summary>
internal sealed class TestIdentity(UserId? user) : ICurrentUser
{
    public UserId? User { get; set; } = user;
    public ValueTask<UserId?> GetUserIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(User);
    }
}

/// <summary>Provides controlled UTC time for deterministic ordering, timestamp, and clock-regression tests.</summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Stores immutable snapshots in memory to exercise repository ownership, paging, and revision contracts.</summary>
/// <remarks>Test-only fault injection supports foreign read results and writes paused before their revision check.</remarks>
internal sealed class TestStore : IConversationRepository, IConnectionProfileRepository, IUserPreferencesRepository
{
    private readonly Dictionary<Guid, Conversation> conversations = [];
    private readonly Dictionary<Guid, ConnectionProfile> profiles = [];
    private readonly Dictionary<UserId, UserPreferences> preferences = [];
    private readonly object sync = new();

    public bool ReturnForeignData { get; set; }
    public CancellationToken LastToken { get; private set; }
    public Func<CancellationToken, Task>? BeforeConversationSave { get; set; }

    public Task<Conversation?> FindAsync(UserId ownerId, Guid conversationId, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        conversations.TryGetValue(conversationId, out var value);
        return Task.FromResult(value is not null && (value.OwnerId == ownerId || ReturnForeignData) ? value : null);
    }

    public Task<Page<ConversationEntry>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        var records = conversations.Values.Where(value => value.OwnerId == ownerId || ReturnForeignData)
            .OrderBy(value => value.CreatedAt).ThenBy(value => value.Id)
            .Select(value => new ConversationEntry(value.Id, value.OwnerId, value.Title, value.CreatedAt, value.UpdatedAt, value.Revision));
        return Task.FromResult(Paginate(records, page));
    }

    public async Task SaveAsync(UserId ownerId, Conversation conversation, long expectedRevision, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        if (BeforeConversationSave is not null)
        {
            await BeforeConversationSave(cancellationToken);
        }

        lock (sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            conversations.TryGetValue(conversation.Id, out var previous);
            RequireOwner(ownerId, conversation.OwnerId);
            if (previous is not null)
            {
                RequireOwner(ownerId, previous.OwnerId);
            }

            if ((previous?.Revision ?? 0) != expectedRevision || conversation.Revision != expectedRevision + 1)
            {
                throw new ChatOperationException(ChatErrorCode.Conflict);
            }

            conversations[conversation.Id] = conversation;
        }
    }

    public Task DeleteAsync(UserId ownerId, Guid conversationId, long expectedRevision, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        lock (sync)
        {
            if (!conversations.TryGetValue(conversationId, out var previous))
            {
                throw new ChatOperationException(ChatErrorCode.NotFound);
            }

            RequireOwner(ownerId, previous.OwnerId);
            if (previous.Revision != expectedRevision)
            {
                throw new ChatOperationException(ChatErrorCode.Conflict);
            }

            conversations.Remove(conversationId);
        }

        return Task.CompletedTask;
    }

    Task<ConnectionProfile?> IConnectionProfileRepository.FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        profiles.TryGetValue(profileId, out var value);
        return Task.FromResult(value is not null && (value.OwnerId == ownerId || ReturnForeignData) ? value : null);
    }

    Task<Page<ConnectionProfile>> IConnectionProfileRepository.ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        return Task.FromResult(Paginate(profiles.Values.Where(value => value.OwnerId == ownerId || ReturnForeignData)
            .OrderBy(value => value.CreatedAt).ThenBy(value => value.Id), page));
    }

    public Task SaveAsync(UserId ownerId, ConnectionProfile profile, bool isNew, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        RequireOwner(ownerId, profile.OwnerId);
        profiles.TryGetValue(profile.Id, out var previous);
        if (previous is not null)
        {
            RequireOwner(ownerId, previous.OwnerId);
        }

        if (isNew && previous is not null)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        if (!isNew && previous is null)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        profiles[profile.Id] = profile;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        if (!profiles.TryGetValue(profileId, out var previous))
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        RequireOwner(ownerId, previous.OwnerId);
        profiles.Remove(profileId);
        if (preferences.TryGetValue(ownerId, out var current) && current.SelectedProfileId == profileId)
        {
            preferences[ownerId] = new UserPreferences(ownerId, current.Theme);
        }

        return Task.CompletedTask;
    }

    public Task<UserPreferences?> FindAsync(UserId ownerId, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        preferences.TryGetValue(ownerId, out var value);
        return Task.FromResult(value ?? (ReturnForeignData ? preferences.Values.FirstOrDefault() : null));
    }

    public Task SaveAsync(UserId ownerId, UserPreferences value, CancellationToken cancellationToken)
    {
        Check(cancellationToken);
        RequireOwner(ownerId, value.OwnerId);
        if (value.SelectedProfileId.HasValue)
        {
            if (!profiles.TryGetValue(value.SelectedProfileId.Value, out var profile))
            {
                throw new ChatOperationException(ChatErrorCode.NotFound);
            }

            RequireOwner(ownerId, profile.OwnerId);
        }

        preferences[ownerId] = value;
        return Task.CompletedTask;
    }

    /// <summary>Creates a detached page from an already owner-filtered, ordered source, reading one extra item for HasMore.</summary>
    private static Page<T> Paginate<T>(IEnumerable<T> source, PageRequest page)
    {
        var window = source.Skip(page.Offset).Take(page.Limit + 1).ToArray();
        return new Page<T>(Array.AsReadOnly(window.Take(page.Limit).ToArray()), window.Length > page.Limit);
    }

    /// <summary>Records the forwarded token for assertions and rejects cancelled operations before accessing test storage.</summary>
    private void Check(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Models the storage boundary's ownership check, reporting foreign records as NotFound.</summary>
    private static void RequireOwner(UserId expected, UserId actual)
    {
        if (expected != actual)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
    }
}
