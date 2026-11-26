using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;
using OpenAI;
using OpenAI.Chat;

namespace Marmotte.ChatUi.Infrastructure.OpenAI;

/// <summary>Translates owned text-chat, discovery, and connection probes to the OpenAI Chat Completions subset.</summary>
/// <remarks>
/// Register once per user scope, sharing that instance between its three ports. Inject a credential-free HttpClient
/// with redirects, cookies, retries, and body logging disabled; its owner disposes it. Every call reauthorizes the
/// persisted profile and resolves its credential anew. No SDK client or credential is cached. History authorization
/// remains the caller's responsibility. Images, tools, Responses, and Files are not implemented.
/// </remarks>
public sealed class OpenAIModelAdapter : IResponseGenerator, IModelCapabilities, IModelConnection
{
    private readonly HttpClient httpClient;
    private readonly ProfileAccess access;
    private readonly OpenAIAdapterOptions options;
    private readonly TimeProvider timeProvider;
    private StreamingVerification? verification;

    /// <summary>Composes transport and trusted user-scoped repositories without taking ownership of dependencies.</summary>
    /// <param name="httpClient">Host-owned transport, with no default credentials or automatic redirects/retries.</param>
    /// <param name="currentUser">Trusted current identity, resolved for each operation.</param>
    /// <param name="profiles">Owner-scoped persisted settings.</param>
    /// <param name="credentials">Separate protected model credentials.</param>
    /// <param name="options">Finite operation deadlines; defaults to two minutes.</param>
    /// <param name="timeProvider">Clock for short-lived capability evidence; defaults to the system clock.</param>
    public OpenAIModelAdapter(HttpClient httpClient, ICurrentUser currentUser, IConnectionProfileRepository profiles,
        IModelCredentialStore credentials, OpenAIAdapterOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(credentials);
        this.httpClient = httpClient;
        access = new ProfileAccess(currentUser, profiles, credentials);
        this.options = options ?? new OpenAIAdapterOptions();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Returns recent streaming evidence for exactly this owned profile/model; image support remains unknown.</summary>
    /// <remarks>No inference is made from model names or discovery. Evidence expires after five minutes or scope disposal.</remarks>
    /// <param name="profile">Owned, persisted settings to reauthorize.</param>
    /// <param name="cancellationToken">Cancels identity and credential resolution.</param>
    /// <returns>The independently established capability states.</returns>
    public async Task<ModelCapabilities> GetAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        await AuthorizeAsync(profile, deadline.Token, cancellationToken).ConfigureAwait(false);
        return Capabilities(profile);
    }

    /// <summary>Lists opaque model identifiers. HTTP 404/405/501 permits manual entry; other failures stay distinct.</summary>
    /// <inheritdoc cref="IModelConnection.DiscoverAsync"/>
    public async Task<ModelDiscoveryResult> DiscoverAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        var credential = await AuthorizeAsync(profile, deadline.Token, cancellationToken).ConfigureAwait(false);
        ValidateProfile(profile);
        try
        {
            var result = await CreateClient(profile, credential).GetOpenAIModelClient().GetModelsAsync(deadline.Token).ConfigureAwait(false);
            using var envelope = JsonDocument.Parse(result.GetRawResponse().Content);
            if (!envelope.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return new(CapabilitySupport.Unknown, [], GenerationFailure.Unknown);
            }

            if (data.EnumerateArray().Any(model => model.ValueKind != JsonValueKind.Object
                || !model.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || !ValidModelId(id.GetString())))
            {
                return new(CapabilitySupport.Unknown, [], GenerationFailure.Unknown);
            }

            var ids = data.EnumerateArray().Select(model => model.GetProperty("id").GetString()!).ToArray();
            return new(CapabilitySupport.Supported, Array.AsReadOnly(ids.Distinct(StringComparer.Ordinal).ToArray()));
        }
        catch (ClientResultException exception) when (exception.Status is 404 or 405 or 501)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(CapabilitySupport.Unsupported, []);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(CapabilitySupport.Unknown, [], MapFailure(exception, false));
        }
    }

    /// <summary>Sends a fixed text prompt without history; successful streaming establishes short-lived capability evidence.</summary>
    /// <remarks>A failed probe does not establish unsupported vision or streaming. No response text is returned or logged.</remarks>
    /// <inheritdoc cref="IModelConnection.CheckAsync"/>
    public async Task<ModelConnectionResult> CheckAsync(ConnectionProfile profile, bool streaming, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var message = new Message(Guid.NewGuid(), Guid.NewGuid(), 1, MessageRole.User, "Reply with OK.", MessageState.Completed, now, now);
        ResponseDelta? terminal = null;
        var hasText = false;
        await foreach (var delta in GenerateCoreAsync(new(profile, [message], streaming), true, cancellationToken).ConfigureAwait(false))
        {
            hasText |= delta.Text.Length != 0;
            terminal = delta;
        }

        var succeeded = terminal?.State == MessageState.Completed || (hasText && terminal?.Failure == GenerationFailure.ContextLimit);
        if (streaming)
        {
            Volatile.Write(ref verification, succeeded ? new(profile, timeProvider.GetUtcNow().AddMinutes(5)) : null);
        }

        return new(succeeded, Capabilities(profile), succeeded ? null : terminal?.Failure ?? GenerationFailure.Unknown);
    }

    /// <summary>Produces ordered text and one terminal result, retaining partial output on provider failure.</summary>
    /// <remarks>
    /// Cancellation throws; timeout returns Unavailable before text and Interrupted after text. Streaming requires a
    /// successful streaming check in this scope. Invalid input/authorization raises a safe ChatOperationException.
    /// Early disposal closes the SDK response; neither complete nor partial requests are retried automatically.
    /// </remarks>
    /// <param name="request">Owned profile, authorized ordered history, and explicit streaming selection.</param>
    /// <param name="cancellationToken">Cancels authorization and generation, including enumeration.</param>
    /// <returns>A lazy, single-consumer stream; no request is sent before enumeration.</returns>
    public IAsyncEnumerable<ResponseDelta> GenerateAsync(ResponseRequest request, CancellationToken cancellationToken) =>
        GenerateCoreAsync(request, false, cancellationToken);

    /// <summary>Authorizes a request and contains all SDK enumeration failures inside the provider-neutral stream.</summary>
    private async IAsyncEnumerable<ResponseDelta> GenerateCoreAsync(ResponseRequest request, bool probe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var deadline = CreateDeadline(cancellationToken);
        var credential = await AuthorizeAsync(request.Profile, deadline.Token, cancellationToken).ConfigureAwait(false);
        ValidateProfile(request.Profile);
        var history = ValidateHistory(request.History);
        if (request.Stream && !probe && Capabilities(request.Profile).Streaming != CapabilitySupport.Supported)
        {
            throw new ChatOperationException(ChatErrorCode.UnsupportedCapability);
        }

        var client = CreateClient(request.Profile, credential).GetChatClient(request.Profile.Model);
        var completionOptions = new ChatCompletionOptions
        {
            Temperature = (float?)request.Profile.Options.Temperature,
            MaxOutputTokenCount = probe ? Math.Min(request.Profile.Options.MaxOutputTokens ?? 256, 256) : request.Profile.Options.MaxOutputTokens
        };

        await using var updates = ReadProviderAsync(client, history, completionOptions, request.Stream, deadline.Token).GetAsyncEnumerator(deadline.Token);
        var hasText = false;
        while (true)
        {
            ResponseDelta? next = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await updates.MoveNextAsync().ConfigureAwait(false))
                {
                    next = updates.Current;
                }
            }
            catch (Exception exception) when (IsProviderFailure(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                next = new("", MessageState.Failed, MapFailure(exception, hasText));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (next is null)
            {
                yield return new("", MessageState.Failed, GenerationFailure.Interrupted);
                yield break;
            }

            hasText |= next.Text.Length != 0;
            yield return next;
            if (next.State != MessageState.Streaming)
            {
                yield break;
            }
        }
    }

    /// <summary>Maps SDK text events, requiring a finish reason instead of treating a disconnected stream as completed.</summary>
    private static async IAsyncEnumerable<ResponseDelta> ReadProviderAsync(ChatClient client, ChatMessage[] history,
        ChatCompletionOptions options, bool streaming, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!streaming)
        {
            var result = await client.CompleteChatAsync(history, options, cancellationToken).ConfigureAwait(false);
            using var envelope = JsonDocument.Parse(result.GetRawResponse().Content);
            if (!envelope.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() != 1 || !choices[0].TryGetProperty("finish_reason", out var reason)
                || reason.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(reason.GetString()))
            {
                yield return new("", MessageState.Failed, GenerationFailure.Unknown);
                yield break;
            }

            var completion = result.Value;
            var text = string.Concat(completion.Content.Select(part => part.Text));
            yield return Finish(text, completion.FinishReason, text.Length != 0, !string.IsNullOrEmpty(completion.Refusal));
            yield break;
        }

        var hasText = false;
        await foreach (var update in client.CompleteChatStreamingAsync(history, options, cancellationToken).ConfigureAwait(false))
        {
            var text = string.Concat(update.ContentUpdate.Select(part => part.Text));
            hasText |= text.Length != 0;
            if (!string.IsNullOrEmpty(update.RefusalUpdate) || update.ToolCallUpdates.Count != 0)
            {
                yield return new(text, MessageState.Failed, GenerationFailure.Rejected);
                yield break;
            }

            if (update.FinishReason.HasValue)
            {
                yield return Finish(text, update.FinishReason.Value, hasText, false);
                yield break;
            }

            if (text.Length != 0)
            {
                yield return new(text, MessageState.Streaming);
            }
        }
    }

    /// <summary>Preserves final text while distinguishing natural completion, truncation, refusal, and unsupported tools.</summary>
    private static ResponseDelta Finish(string text, ChatFinishReason reason, bool hasText, bool refused) =>
        refused ? new(text, MessageState.Failed, GenerationFailure.Rejected) : reason switch
        {
            ChatFinishReason.Stop when hasText => new(text, MessageState.Completed),
            ChatFinishReason.Length => new(text, MessageState.Failed, GenerationFailure.ContextLimit),
            ChatFinishReason.ContentFilter or ChatFinishReason.ToolCalls => new(text, MessageState.Failed, GenerationFailure.Rejected),
            _ => new(text, MessageState.Failed, GenerationFailure.Unknown)
        };

    /// <summary>Creates a request-local client, preserving the configured API base path and disabling SDK retries and diagnostics.</summary>
#pragma warning disable OPENAI001 // Pinned SDK's custom-auth constructor allows a truly keyless request; covered by wire tests.
    private OpenAIClient CreateClient(ConnectionProfile profile, ModelCredential? credential) => new(
        new ProfileAuthenticationPolicy(credential), new OpenAIClientOptions
        {
            Endpoint = new Uri(profile.Endpoint.AbsoluteUri.TrimEnd('/') + "/"),
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(0),
            NetworkTimeout = options.OperationTimeout,
            EnableDistributedTracing = false,
            ClientLoggingOptions = new ClientLoggingOptions { EnableLogging = false, EnableMessageContentLogging = false }
        });
#pragma warning restore OPENAI001

    /// <summary>Separates caller cancellation from an internal deadline while resolving trusted storage dependencies.</summary>
    private async Task<ModelCredential?> AuthorizeAsync(ConnectionProfile profile, CancellationToken deadline, CancellationToken caller)
    {
        try
        {
            return await access.AuthorizeAsync(profile, deadline).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new ChatOperationException(ChatErrorCode.StorageUnavailable);
        }
    }

    /// <summary>Creates a finite deadline; disposal releases its timer and linked cancellation registration.</summary>
    private CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.OperationTimeout);
        return deadline;
    }

    /// <summary>Returns only exact, unexpired evidence; a successful model list never implies vision support.</summary>
    private ModelCapabilities Capabilities(ConnectionProfile profile)
    {
        var evidence = Volatile.Read(ref verification);
        var verified = evidence is not null && evidence.ExpiresAt > timeProvider.GetUtcNow()
            && evidence.Profile.OwnerId == profile.OwnerId && evidence.Profile.Id == profile.Id
            && evidence.Profile.Endpoint == profile.Endpoint && evidence.Profile.Model == profile.Model
            && evidence.Profile.UpdatedAt == profile.UpdatedAt
            && evidence.Profile.Options.Temperature == profile.Options.Temperature
            && evidence.Profile.Options.MaxOutputTokens == profile.Options.MaxOutputTokens;
        return new(verified ? CapabilitySupport.Supported : CapabilitySupport.Unknown, CapabilitySupport.Unknown);
    }

    /// <summary>Rejects unsupported option ranges and malformed opaque identifiers before sending anything.</summary>
    private static void ValidateProfile(ConnectionProfile profile)
    {
        if (!ValidModelId(profile.Model) || profile.Options.Temperature is > 2)
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }
    }

    /// <summary>Accepts nonblank, bounded identifiers without controls, preserving provider-specific punctuation and spaces.</summary>
    private static bool ValidModelId(string? model) => !string.IsNullOrWhiteSpace(model)
        && model.Length <= 256 && !model.Any(char.IsControl);

    /// <summary>Snapshots ordered text history and rejects active placeholders and all unprepared attachments.</summary>
    private static ChatMessage[] ValidateHistory(IReadOnlyList<Message> history)
    {
        if (history is null || history.Count == 0)
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }

        var snapshot = history.ToArray();
        if (snapshot.Any(message => message is null || message.IsActive)
            || snapshot[^1].Role != MessageRole.User
            || snapshot.Zip(snapshot.Skip(1)).Any(pair => pair.First.Sequence >= pair.Second.Sequence)
            || snapshot.Select(message => message.Id).Distinct().Count() != snapshot.Length)
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }

        if (snapshot.Any(message => message.Attachments.Count != 0))
        {
            throw new ChatOperationException(ChatErrorCode.UnsupportedCapability);
        }

        return snapshot.Select<Message, ChatMessage>(message => message.Role == MessageRole.User
            ? new UserChatMessage(message.Content) : new AssistantChatMessage(message.Content)).ToArray();
    }

    /// <summary>Limits exception translation to transport, SDK, and malformed-wire failures; authorization faults propagate safely.</summary>
    private static bool IsProviderFailure(Exception exception) => exception is ClientResultException or HttpRequestException
        or IOException or JsonException or FormatException or InvalidOperationException or ArgumentException or OperationCanceledException;

    /// <summary>Discards provider text and exception chains; context limits are recognized only by a structured error code.</summary>
    private static GenerationFailure MapFailure(Exception exception, bool hasText)
    {
        if (hasText)
        {
            return GenerationFailure.Interrupted;
        }

        if (exception is ClientResultException result)
        {
            try
            {
                var content = result.GetRawResponse()?.Content;
                if (content is not null)
                {
                    using var json = JsonDocument.Parse(content);
                    if (json.RootElement.ValueKind == JsonValueKind.Object
                        && json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                        && code.GetString() == "context_length_exceeded")
                    {
                        return GenerationFailure.ContextLimit;
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON error pages are still classified by HTTP status, never by their text.
            }

            return result.Status is >= 400 and < 500 and not 408 and not 429 ? GenerationFailure.Rejected : GenerationFailure.Unavailable;
        }

        return exception is HttpRequestException or IOException or OperationCanceledException ? GenerationFailure.Unavailable : GenerationFailure.Unknown;
    }

    /// <summary>Retains at most one exact profile's successful streaming probe for five minutes, without storing credentials.</summary>
    private sealed record StreamingVerification(ConnectionProfile Profile, DateTimeOffset ExpiresAt);
}
