using System.Net;
using System.Text.Json;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using static Marmotte.ChatUi.OpenAI.Tests.AdapterHarness;

namespace Marmotte.ChatUi.OpenAI.Tests;

/// <summary>Exercises the real SDK through controlled HTTP, checking transport contracts rather than SDK mocks.</summary>
public sealed class AdapterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_preserves_endpoint_history_options_and_optional_authentication(bool authenticated)
    {
        using var harness = new AdapterHarness(async (request, token) =>
        {
            Assert.Equal("http://model.test/custom/api/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(authenticated ? "Bearer test-key" : null, request.Headers.Authorization?.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = body.RootElement;
            Assert.Equal("local-model", root.GetProperty("model").GetString());
            Assert.Equal(0.5, root.GetProperty("temperature").GetDouble());
            Assert.Equal(64, root.GetProperty("max_completion_tokens").GetInt32());
            var messages = root.GetProperty("messages");
            Assert.Equal(3, messages.GetArrayLength());
            Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
            Assert.False(root.TryGetProperty("stream", out var stream) && stream.GetBoolean());
            return Completion();
        });
        harness.Store.Credential = authenticated ? new ModelCredential("test-key") : null;
        var earlier = new Message(Guid.NewGuid(), Guid.NewGuid(), 2, MessageRole.Assistant, "Earlier answer", MessageState.Completed,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var deltas = await CollectAsync(harness.Adapter.GenerateAsync(new(harness.Profile, [UserMessage(), earlier, UserMessage(3)]), Token));
        Assert.Equal(new ResponseDelta("Hello back", MessageState.Completed), Assert.Single(deltas));
        Assert.Equal(1, harness.Handler.Requests);
    }

    [Fact]
    public async Task Streaming_probe_enables_exact_profile_and_preserves_order_and_one_terminal()
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Streaming()));
        Assert.Equal(CapabilitySupport.Unknown, (await harness.Adapter.GetAsync(harness.Profile, Token)).Streaming);
        var check = await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        Assert.True(check.Succeeded);
        Assert.Equal(CapabilitySupport.Supported, check.Capabilities.Streaming);
        Assert.Equal(CapabilitySupport.Unknown, check.Capabilities.ImageInput);
        var deltas = await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(true), Token));
        Assert.Equal("Hello back!", string.Concat(deltas.Select(delta => delta.Text)));
        Assert.Equal(MessageState.Completed, deltas[^1].State);
        Assert.Single(deltas, delta => delta.State != MessageState.Streaming);
    }

    [Fact]
    public async Task Discovery_is_optional_and_does_not_claim_capabilities_or_filter_manual_models()
    {
        var calls = 0;
        using var harness = new AdapterHarness((request, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? Json("{\"object\":\"list\",\"data\":[{\"id\":\"different-model\",\"object\":\"model\",\"created\":0,\"owned_by\":\"local\"}]}")
                : Completion());
        });
        var discovery = await harness.Adapter.DiscoverAsync(harness.Profile, Token);
        Assert.Equal(CapabilitySupport.Supported, discovery.Support);
        Assert.Equal("different-model", Assert.Single(discovery.Models));
        Assert.Equal(CapabilitySupport.Unknown, (await harness.Adapter.GetAsync(harness.Profile, Token)).ImageInput);
        Assert.True((await harness.Adapter.CheckAsync(harness.Profile, false, Token)).Succeeded);
    }

    [Theory]
    [InlineData(404, CapabilitySupport.Unsupported, null)]
    [InlineData(405, CapabilitySupport.Unsupported, null)]
    [InlineData(501, CapabilitySupport.Unsupported, null)]
    [InlineData(401, CapabilitySupport.Unknown, GenerationFailure.Rejected)]
    [InlineData(503, CapabilitySupport.Unknown, GenerationFailure.Unavailable)]
    public async Task Discovery_distinguishes_unsupported_from_authentication_and_outage(int status, CapabilitySupport support, GenerationFailure? failure)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Json("{}", (HttpStatusCode)status)));
        var result = await harness.Adapter.DiscoverAsync(harness.Profile, Token);
        Assert.Equal(support, result.Support);
        Assert.Equal(failure, result.Failure);
        Assert.Empty(result.Models);
        Assert.Equal(1, harness.Handler.Requests);
    }

    [Theory]
    [InlineData(400, GenerationFailure.Rejected)]
    [InlineData(401, GenerationFailure.Rejected)]
    [InlineData(403, GenerationFailure.Rejected)]
    [InlineData(404, GenerationFailure.Rejected)]
    [InlineData(408, GenerationFailure.Unavailable)]
    [InlineData(429, GenerationFailure.Unavailable)]
    [InlineData(500, GenerationFailure.Unavailable)]
    [InlineData(503, GenerationFailure.Unavailable)]
    public async Task Provider_errors_are_safe_and_never_retried(int status, GenerationFailure expected)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Json("{\"error\":{\"message\":\"secret text\",\"code\":\"failure\"}}", (HttpStatusCode)status)));
        var delta = Assert.Single(await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token)));
        Assert.Equal(new ResponseDelta("", MessageState.Failed, expected), delta);
        Assert.Equal(1, harness.Handler.Requests);
    }

    [Fact]
    public async Task Structured_context_error_is_classified_without_matching_error_text()
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Json("{\"error\":{\"code\":\"context_length_exceeded\",\"message\":\"private\"}}", HttpStatusCode.BadRequest)));
        Assert.Equal(GenerationFailure.ContextLimit, Assert.Single(await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token))).Failure);
    }

    [Theory]
    [InlineData("length", GenerationFailure.ContextLimit)]
    [InlineData("content_filter", GenerationFailure.Rejected)]
    [InlineData("tool_calls", GenerationFailure.Rejected)]
    public async Task Non_success_finish_reasons_retain_partial_text(string reason, GenerationFailure expected)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Completion(reason)));
        var delta = Assert.Single(await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token)));
        Assert.Equal("Hello back", delta.Text);
        Assert.Equal(MessageState.Failed, delta.State);
        Assert.Equal(expected, delta.Failure);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"choices\":[]}")]
    public async Task Malformed_response_is_a_safe_failure(string body)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Json(body)));
        Assert.Equal(MessageState.Failed, Assert.Single(await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token))).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dropped_stream_retains_partial_output_and_fails_once(bool ioFailure)
    {
        var calls = 0;
        using var harness = new AdapterHarness((_, _) => Task.FromResult(++calls == 1 ? Streaming() : ioFailure
            ? StreamResponse(new StalledStream(Chunk("partial"), true)) : Sse(Chunk("partial") + "data: [DONE]\n\n")));
        await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        var deltas = await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(true), Token));
        Assert.Equal("partial", string.Concat(deltas.Select(delta => delta.Text)));
        Assert.Equal(new ResponseDelta("", MessageState.Failed, GenerationFailure.Interrupted), deltas[^1]);
        Assert.Single(deltas, delta => delta.State == MessageState.Failed);
        Assert.Equal(2, harness.Handler.Requests);
    }

    [Fact]
    public async Task Cancellation_during_stream_throws_and_disposes_transport()
    {
        var calls = 0;
        using var stalled = new StalledStream(Chunk("partial"));
        using var harness = new AdapterHarness((_, _) => Task.FromResult(++calls == 1 ? Streaming() : StreamResponse(stalled)));
        await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var iterator = harness.Adapter.GenerateAsync(harness.Request(true), cancellation.Token).GetAsyncEnumerator(Token);
        Assert.True(await iterator.MoveNextAsync());
        var pending = iterator.MoveNextAsync().AsTask();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(stalled.Disposed);
    }

    [Fact]
    public async Task Early_disposal_closes_a_live_stream_without_waiting_for_timeout()
    {
        var calls = 0;
        using var stalled = new StalledStream(Chunk("partial"));
        using var harness = new AdapterHarness((_, _) => Task.FromResult(++calls == 1 ? Streaming() : StreamResponse(stalled)));
        await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        var iterator = harness.Adapter.GenerateAsync(harness.Request(true), Token).GetAsyncEnumerator(Token);
        Assert.True(await iterator.MoveNextAsync());
        await iterator.DisposeAsync();
        Assert.True(stalled.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Total_timeout_covers_headers_and_stream_body(bool partial)
    {
        var calls = 0;
        using var stalled = new StalledStream(Chunk("partial"));
        using var harness = new AdapterHarness(async (_, token) =>
        {
            if (++calls == 1)
            {
                return Streaming();
            }

            if (partial)
            {
                return StreamResponse(stalled);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Completion();
        }, TimeSpan.FromMilliseconds(250));
        await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        var deltas = await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(partial), Token)).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal(partial ? GenerationFailure.Interrupted : GenerationFailure.Unavailable, deltas[^1].Failure);
        Assert.Equal(2, harness.Handler.Requests);
    }

    [Fact]
    public async Task Unknown_streaming_is_rejected_before_transport()
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Completion()));
        var error = await Assert.ThrowsAsync<ChatOperationException>(() => CollectAsync(harness.Adapter.GenerateAsync(harness.Request(true), Token)));
        Assert.Equal(ChatErrorCode.UnsupportedCapability, error.Code);
        Assert.Equal(0, harness.Handler.Requests);
    }

    [Fact]
    public async Task Credentials_are_resolved_per_operation_and_not_shared_in_default_headers()
    {
        var headers = new List<string?>();
        using var harness = new AdapterHarness((request, _) =>
        {
            headers.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(Completion());
        });
        harness.Store.Credential = new ModelCredential("first");
        await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token));
        harness.Store.Credential = new ModelCredential("second");
        await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token));
        harness.Store.Credential = null;
        await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token));
        Assert.Equal(["first", "second", null], headers);
        Assert.Null(harness.Client.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData("anonymous", ChatErrorCode.Unauthenticated)]
    [InlineData("foreign", ChatErrorCode.NotFound)]
    [InlineData("deleted", ChatErrorCode.NotFound)]
    [InlineData("modified", ChatErrorCode.Conflict)]
    public async Task Unauthorized_or_modified_profiles_never_send_credentials(string scenario, ChatErrorCode expected)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Completion()));
        var profile = harness.Profile;
        switch (scenario)
        {
            case "anonymous": harness.Store.User = null; break;
            case "foreign": harness.Store.User = new UserId("other"); break;
            case "deleted": harness.Store.Missing = true; break;
            case "modified": profile = new(profile.Id, profile.OwnerId, profile.Name, new Uri("http://attacker.test/"), profile.Model, profile.Options, profile.CreatedAt, profile.UpdatedAt); break;
        }

        var error = await Assert.ThrowsAsync<ChatOperationException>(() => CollectAsync(harness.Adapter.GenerateAsync(new(profile, [UserMessage()]), Token)));
        Assert.Equal(expected, error.Code);
        Assert.Equal(0, harness.Store.CredentialReads);
        Assert.Equal(0, harness.Handler.Requests);
    }

    [Fact]
    public async Task Attachments_are_rejected_instead_of_silently_ignored()
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Completion()));
        var attachment = new Attachment(Guid.NewGuid(), harness.Profile.OwnerId, "image.png", "image/png", 10,
            DateTimeOffset.UnixEpoch, AttachmentState.Ready, 1, 1);
        var message = new Message(Guid.NewGuid(), Guid.NewGuid(), 1, MessageRole.User, "Describe it", MessageState.Completed,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [attachment]);
        var error = await Assert.ThrowsAsync<ChatOperationException>(() => CollectAsync(harness.Adapter.GenerateAsync(new(harness.Profile, [message]), Token)));
        Assert.Equal(ChatErrorCode.UnsupportedCapability, error.Code);
        Assert.Equal(0, harness.Handler.Requests);
    }

    [Theory]
    [InlineData("options")]
    [InlineData("model")]
    [InlineData("history")]
    public async Task Invalid_inputs_fail_before_the_network(string scenario)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Completion()));
        var profile = harness.Profile;
        harness.Store.Profile = new(profile.Id, profile.OwnerId, profile.Name, profile.Endpoint,
            scenario == "model" ? "bad\nmodel" : profile.Model,
            scenario == "options" ? new GenerationOptions(2.1) : profile.Options, profile.CreatedAt, profile.UpdatedAt);
        var request = new ResponseRequest(harness.Profile, scenario == "history" ? [UserMessage(2), UserMessage(1)] : [UserMessage()]);
        var error = await Assert.ThrowsAsync<ChatOperationException>(() => CollectAsync(harness.Adapter.GenerateAsync(request, Token)));
        Assert.Equal(ChatErrorCode.InvalidRequest, error.Code);
        Assert.Equal(0, harness.Handler.Requests);
    }

    [Fact]
    public async Task Capability_evidence_expires_and_never_follows_a_different_profile_or_user()
    {
        var clock = new TestClock();
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Streaming()), clock: clock);
        await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        clock.Now = clock.Now.AddMinutes(6);
        Assert.Equal(CapabilitySupport.Unknown, (await harness.Adapter.GetAsync(harness.Profile, Token)).Streaming);
        await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        var profile = harness.Profile;
        harness.Store.User = new UserId("user-b");
        harness.Store.Profile = new(Guid.NewGuid(), harness.Store.User, profile.Name, profile.Endpoint, profile.Model,
            profile.Options, profile.CreatedAt, profile.UpdatedAt);
        Assert.Equal(CapabilitySupport.Unknown, (await harness.Adapter.GetAsync(harness.Profile, Token)).Streaming);
    }

    [Fact]
    public async Task Cancellation_before_enumeration_never_resolves_credentials_or_sends()
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Completion()));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), cancellation.Token)));
        Assert.Equal(0, harness.Store.CredentialReads);
        Assert.Equal(0, harness.Handler.Requests);
    }

    [Fact]
    public async Task Malformed_stream_keeps_previous_text_and_maps_parser_failure()
    {
        var calls = 0;
        using var harness = new AdapterHarness((_, _) => Task.FromResult(++calls == 1 ? Streaming() : Sse(Chunk("partial") + "data: invalid-json\n\n")));
        await harness.Adapter.CheckAsync(harness.Profile, true, Token);
        var deltas = await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(true), Token));
        Assert.Equal("partial", string.Concat(deltas.Select(delta => delta.Text)));
        Assert.Equal(GenerationFailure.Interrupted, deltas[^1].Failure);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("invalid")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[{}]}")]
    public async Task Malformed_discovery_is_not_a_successful_empty_list(string body)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Json(body)));
        var result = await harness.Adapter.DiscoverAsync(harness.Profile, Token);
        Assert.Equal(CapabilitySupport.Unknown, result.Support);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Missing_finish_reason_is_not_a_completed_response()
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Json("{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"incomplete\"}}]}")));
        var delta = Assert.Single(await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token)));
        Assert.Equal(MessageState.Failed, delta.State);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"private error\"")]
    [InlineData("<html>private error</html>")]
    public async Task Non_object_error_bodies_are_classified_by_status_without_escaping(string body)
    {
        using var harness = new AdapterHarness((_, _) => Task.FromResult(Json(body, HttpStatusCode.Unauthorized)));
        var delta = Assert.Single(await CollectAsync(harness.Adapter.GenerateAsync(harness.Request(), Token)));
        Assert.Equal(new ResponseDelta("", MessageState.Failed, GenerationFailure.Rejected), delta);
    }

    /// <summary>Wraps a test-owned stream in a response so SDK disposal is observable.</summary>
    private static HttpResponseMessage StreamResponse(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new("text/event-stream");
        return response;
    }
}
