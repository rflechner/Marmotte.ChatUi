using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Domain;
using Marmotte.ChatUi.Infrastructure.OpenAI;
using static Marmotte.ChatUi.OpenAI.Tests.AdapterHarness;

namespace Marmotte.ChatUi.OpenAI.Tests;

/// <summary>Opt-in compatibility checks against an explicitly configured local model server; never used in routine CI.</summary>
public sealed class LiveServerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static bool Enabled => Environment.GetEnvironmentVariable("MARMOTTE_RUN_MODEL_TESTS") == "1";

    [Fact(Skip = "Requires MARMOTTE_RUN_MODEL_TESTS=1, MARMOTTE_MODEL_ENDPOINT, and MARMOTTE_MODEL_ID.", SkipUnless = nameof(Enabled))]
    public async Task Configured_server_supports_discovery_text_streaming_and_client_cancellation()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("MARMOTTE_MODEL_ENDPOINT")
            ?? throw new InvalidOperationException("Set MARMOTTE_MODEL_ENDPOINT to the complete API base URL."));
        var model = Environment.GetEnvironmentVariable("MARMOTTE_MODEL_ID")
            ?? throw new InvalidOperationException("Set MARMOTTE_MODEL_ID to an available model.");
        var store = new TestStore();
        store.Profile = new(store.Profile.Id, store.Profile.OwnerId, "Live compatibility", endpoint, model,
            new GenerationOptions(0, 1024), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var key = Environment.GetEnvironmentVariable("MARMOTTE_MODEL_KEY");
        store.Credential = string.IsNullOrWhiteSpace(key) ? null : new ModelCredential(key);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var adapter = new OpenAIModelAdapter(http, store, store, store);
        var discovery = await adapter.DiscoverAsync(store.Profile, Token);
        Assert.Equal(CapabilitySupport.Supported, discovery.Support);
        Assert.Contains(model, discovery.Models);
        var text = await adapter.CheckAsync(store.Profile, false, Token);
        Assert.True(text.Succeeded, $"Text check: {text.Failure}");
        var streaming = await adapter.CheckAsync(store.Profile, true, Token);
        Assert.True(streaming.Succeeded, $"Streaming check: {streaming.Failure}");
        Assert.Equal(CapabilitySupport.Supported, streaming.Capabilities.Streaming);
        Assert.Equal(CapabilitySupport.Unknown, streaming.Capabilities.ImageInput);
        var response = await CollectAsync(adapter.GenerateAsync(new(store.Profile, [UserMessage()], true), Token));
        Assert.Equal(MessageState.Completed, response[^1].State);
        Assert.NotEmpty(string.Concat(response.Select(delta => delta.Text)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var now = DateTimeOffset.UtcNow;
        var longPrompt = new Message(Guid.NewGuid(), Guid.NewGuid(), 1, MessageRole.User,
            "Count from 1 to 100. Output only the numbers.", MessageState.Completed, now, now);
        await using var iterator = adapter.GenerateAsync(new(store.Profile, [longPrompt], true), cancellation.Token).GetAsyncEnumerator(Token);
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(MessageState.Streaming, iterator.Current.State);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => iterator.MoveNextAsync().AsTask());
    }
}
