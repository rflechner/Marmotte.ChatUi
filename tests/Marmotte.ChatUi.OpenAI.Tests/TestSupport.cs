using System.Net;
using System.Text;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using Marmotte.ChatUi.Infrastructure.OpenAI;

namespace Marmotte.ChatUi.OpenAI.Tests;

/// <summary>Owns controlled transport and user-scoped stores for wire-level adapter tests, without external services.</summary>
internal sealed class AdapterHarness : IDisposable
{
    internal TestStore Store { get; } = new();
    internal ScriptedHandler Handler { get; }
    internal HttpClient Client { get; }
    internal OpenAIModelAdapter Adapter { get; }
    internal ConnectionProfile Profile => Store.Profile;

    /// <summary>Creates a fresh scope; scripted handlers must return disposable responses and honor cancellation.</summary>
    internal AdapterHarness(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null,
        TimeProvider? clock = null)
    {
        Handler = new ScriptedHandler(send);
        Client = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan };
        Adapter = new(Client, Store, Store, Store, new OpenAIAdapterOptions(timeout), clock);
    }

    /// <summary>Returns a valid user-only history for the harness's current profile.</summary>
    internal ResponseRequest Request(bool streaming = false) => new(Profile, [UserMessage()], streaming);

    /// <summary>Creates a completed user message with fixed harmless text.</summary>
    internal static Message UserMessage(long sequence = 1) => new(Guid.NewGuid(), Guid.NewGuid(), sequence, MessageRole.User,
        "Hello", MessageState.Completed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    /// <summary>Fully consumes a generation, preserving observable order.</summary>
    internal static async Task<List<ResponseDelta>> CollectAsync(IAsyncEnumerable<ResponseDelta> stream)
    {
        var results = new List<ResponseDelta>();
        await foreach (var delta in stream)
        {
            results.Add(delta);
        }

        return results;
    }

    /// <summary>Produces a wire-format chat completion with selectable terminal reason.</summary>
    internal static HttpResponseMessage Completion(string reason = "stop") => Json(
        "{\"id\":\"chat-1\",\"object\":\"chat.completion\",\"created\":0,\"model\":\"local-model\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Hello back\"},\"finish_reason\":\"" + reason + "\"}]}");

    /// <summary>Creates an SSE chunk without assuming a single TCP packet per event.</summary>
    internal static string Chunk(string text, string? finish = null) =>
        "data: {\"id\":\"chat-1\",\"object\":\"chat.completion.chunk\",\"created\":0,\"model\":\"local-model\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + text + "\"},\"finish_reason\":" + (finish is null ? "null" : "\"" + finish + "\"") + "}]}\n\n";

    /// <summary>Produces a complete SSE response suitable for successful capability verification.</summary>
    internal static HttpResponseMessage Streaming() => Sse(Chunk("Hello ") + Chunk("back") + Chunk("!", "stop") + "data: [DONE]\n\n");

    /// <summary>Creates an owned JSON response, including intentionally invalid bodies for protocol tests.</summary>
    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    /// <summary>Creates an owned SSE response.</summary>
    internal static HttpResponseMessage Sse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
    };

    /// <summary>Releases the harness-owned client and handler; adapter dependencies are not owned by the adapter.</summary>
    public void Dispose() => Client.Dispose();
}

/// <summary>Runs one controlled response factory per HTTP attempt and counts attempts to detect unintended retries.</summary>
internal sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    internal int Requests { get; private set; }

    /// <summary>Forwards the actual SDK-generated request and transport token to the test script.</summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return send(request, cancellationToken);
    }
}

/// <summary>Provides trusted identity and mutable test settings; no production persistence behavior is implied.</summary>
internal sealed class TestStore : ICurrentUser, IConnectionProfileRepository, IModelCredentialStore
{
    internal UserId? User { get; set; } = new("user-a");
    internal ConnectionProfile Profile { get; set; } = new(Guid.NewGuid(), new UserId("user-a"), "Local",
        new Uri("http://model.test/custom/api"), "local-model", new GenerationOptions(0.5, 64), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    internal ModelCredential? Credential { get; set; }
    internal int CredentialReads { get; private set; }
    internal bool Missing { get; set; }

    /// <summary>Resolves the current test identity without caching it.</summary>
    public ValueTask<UserId?> GetUserIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(User);
    }

    /// <summary>Returns only the current owned profile unless a test requests its deletion.</summary>
    public Task<ConnectionProfile?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult(!Missing && ownerId == Profile.OwnerId && profileId == Profile.Id ? Profile : null);

    /// <summary>Resolves the current credential only for the owned profile and counts accesses.</summary>
    Task<ModelCredential?> IModelCredentialStore.FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken)
    {
        Assert.Equal(Profile.OwnerId, ownerId);
        Assert.Equal(Profile.Id, profileId);
        CredentialReads++;
        return Task.FromResult(Credential);
    }

    /// <summary>Listing is outside these read-only adapter tests.</summary>
    public Task<Page<ConnectionProfile>> ListAsync(UserId ownerId, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();
    /// <summary>Writes are outside these read-only adapter tests.</summary>
    public Task SaveAsync(UserId ownerId, ConnectionProfile profile, bool isNew, CancellationToken cancellationToken) => throw new NotSupportedException();
    /// <summary>Deletion is outside these read-only adapter tests.</summary>
    public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
    /// <summary>Credential replacement is simulated through the test-only property.</summary>
    public Task SaveAsync(UserId ownerId, Guid profileId, ModelCredential credential, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Delivers an SSE prefix, then stalls until cancelled or throws an I/O failure; records early disposal.</summary>
internal sealed class StalledStream(string prefix, bool disconnect = false) : Stream
{
    private readonly byte[] bytes = Encoding.UTF8.GetBytes(prefix);
    private int position;
    internal bool Disposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => position; set => throw new NotSupportedException(); }

    /// <summary>Returns small fragments to exercise UTF-8/SSE buffering, then observes the transport's cancellation token.</summary>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (position < bytes.Length)
        {
            var count = Math.Min(Math.Min(7, buffer.Length), bytes.Length - position);
            bytes.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        if (disconnect)
        {
            throw new IOException("secret provider body must not escape");
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    /// <summary>Delegates older async stream reads to the token-aware implementation.</summary>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Records disposal even when enumeration ends before the synthetic stream does.</summary>
    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Advances capability expiry deterministically without sleeping.</summary>
internal sealed class TestClock : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => Now;
}
