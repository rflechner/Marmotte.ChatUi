# OpenAI-compatible model adapter

English | [Français](models.fr.md)

## Implemented scope and SDK decision

M6 adds `OpenAIModelAdapter` in Infrastructure.OpenAI and registers it in Web when `ConnectionStrings:chatdb` is configured. It implements `IResponseGenerator`, `IModelConnection`, and `IModelCapabilities` without exposing SDK types. M8 connects the chat UI, generation persistence and settings screens; see [chat](chat.md). Components and the application core have no SDK dependency.

[OpenAI 2.15.0](https://www.nuget.org/packages/OpenAI/2.15.0) is pinned. The adapter uses Chat Completions and Models only, with configurable endpoints. Its custom authentication policy sends a Bearer header only when an owned credential exists; no placeholder key is invented. The SDK's custom-auth constructor is experimental (`OPENAI001`): suppression is confined to client creation, with wire tests guarding this choice. SDK upgrades must rerun these tests. SDK retries, logging, content logging, and distributed tracing are disabled; host HTTP telemetry still records ordinary request metadata without bodies or credentials.

The [SDK documentation](https://github.com/openai/openai-dotnet#using-a-custom-base-url-and-api-key), [LM Studio API](https://lmstudio.ai/docs/developer/openai-compat), [LM Studio authentication](https://lmstudio.ai/docs/developer/core/authentication), and [Unsloth API](https://unsloth.ai/docs/basics/api) informed the evaluation. Documentation is not proof of runtime compatibility: only the checks below were performed.

## Profiles, ownership, and transport

Persist the full API base URL, including the server's actual path. The adapter appends `chat/completions` or `models`; it never adds `/v1`, changes a port, or probes guessed addresses. A trailing slash is normalized. For the tested LM Studio instance, the base URL is `http://127.0.0.1:1234/v1`, and the model is `lfm3-1.7b`. These are tested settings, not defaults for other installations.

Domain profiles require HTTP(S), with no user info, query, or fragment. The adapter accepts opaque nonblank model IDs up to 256 characters without controls, temperature from 0 through 2, and positive optional output limits. Null options omit the fields and use server defaults. The output limit is serialized as `max_completion_tokens`; unsupported options fail safely rather than being dropped or automatically retried. Discovery is optional: a manually supplied model need not appear in the list. Server-specific option ranges and model context windows still depend on the selected server/model.

Before every operation, the adapter resolves trusted `ICurrentUser`, verifies the persisted profile owner, and compares its endpoint, model, options, and update timestamp with the supplied snapshot. A missing/foreign profile yields `NotFound`; modified/stale settings yield `Conflict`, before credential retrieval or network access. Credentials are loaded anew from `IModelCredentialStore`. The caller must separately authorize conversation history; these are server-only ports and must not accept arbitrary browser-supplied histories or profile objects.

Web registers one scoped adapter instance behind all three ports. Request-local SDK clients contain the credential and are not cached. The shared HTTP transport has no default credentials, cookies, redirects, request loggers, or retries. Redirects are deliberately not followed, including same-origin redirects: configure the final API base URL. Direct consumers own their injected HttpClient and must preserve these transport rules. Allowing HTTP/private addresses supports local servers; the host must restrict who can configure endpoints and set an egress policy appropriate to its deployment. No production SSRF allowlist or DNS filtering is claimed.

`Models:OperationTimeoutSeconds` defaults to 120 and must be greater than zero and at most 1800. A linked deadline covers authorization and the entire HTTP operation, including stalled streamed bodies. The host client's default 30-second timeout is replaced by the adapter deadline. A deadline during identity/storage resolution yields `StorageUnavailable`; caller cancellation remains `OperationCanceledException`.

## Calls and lifecycle

| Contract | Behavior |
| --- | --- |
| `DiscoverAsync(profile, token)` | Calls Models; returns deduplicated opaque IDs. HTTP 404/405/501 means Unsupported and permits manual entry. Authentication/outage failures are Unknown with a safe failure, not an empty successful list. |
| `CheckAsync(profile, streaming, token)` | Sends the fixed prompt `Reply with OK.` with at most 256 output tokens (or the profile's smaller limit), no history, and no returned/generated text logging. It consumes inference resources. Truncation with visible text can still establish connectivity; truncation before text fails the check. |
| `GetAsync(profile, token)` | Returns recent evidence, not a guessed model capability. A successful streaming check establishes Supported for that exact owner/profile/endpoint/model/update time for five minutes. At most one profile's evidence is retained per scope. Other profiles, expired evidence, and new scopes return Unknown. |
| `GenerateAsync(ResponseRequest(profile, history, Stream), token)` | `Stream` defaults to false, producing one terminal result. True requires a successful streaming check in the same scope. No automatic fallback starts another generation. |

History is copied before transport, ordered by strictly increasing sequence, ends in a user message, and excludes pending/streaming assistant placeholders. The caller explicitly chooses whether to include earlier cancelled/failed partial answers. Every attachment is rejected with `UnsupportedCapability`: M6 has no binary preparation path, and nothing is silently discarded. ImageInput remains Unknown; neither a model name nor Models discovery establishes vision support. Responses, Files, tools, audio, and images are not implemented.

Streaming yields ordered text deltas and exactly one terminal update. Natural stop completes; missing finish reason, malformed SSE, or disconnect fails while retaining previously yielded text. Length termination maps to ContextLimit and retains text; refusal/content filtering/tool calls fail safely. Reasoning-only provider extensions are not exposed as answer text. Caller cancellation throws and iterator disposal releases transport resources; the caller separately persists cancellation with a live token. Cancellation closes the client response, but stopping computation on the server depends on that server.

| Failure | Mapping |
| --- | --- |
| Structured `context_length_exceeded`, or length finish reason | ContextLimit |
| HTTP 4xx except 408/429, including rejected credentials or unknown model | Rejected |
| Network failure, HTTP 408/429/5xx, or timeout before text | Unavailable |
| Transport/protocol failure or timeout after streamed text | Interrupted |
| Malformed non-streaming payload or unrecognized completion | Unknown |

No raw provider body, exception message, credential, or conversation text is returned in errors. There is no automatic retry, even for HTTP 429/500/503. Reload/reconcile persisted generation state before any explicit user retry. M8 `ChatService` records accepted turns, batched deltas and terminal responses in conversations.

## Verification and remaining compatibility work

Controlled HTTP tests execute the actual SDK with synthetic responses, without a paid key or personal server. They cover request paths, history/options, optional and rotating credentials, user/profile isolation, discovery/manual entry, finish reasons, safe errors, no retries, malformed/truncated streams, cancellation, total deadlines, early disposal, capability expiry, and attachment rejection.

The opt-in test uses an explicitly configured server. After the usual restore and Release build:

```sh
MARMOTTE_RUN_MODEL_TESTS=1 \
MARMOTTE_MODEL_ENDPOINT=http://127.0.0.1:1234/v1 \
MARMOTTE_MODEL_ID=lfm3-1.7b \
dotnet test --project tests/Marmotte.ChatUi.OpenAI.Tests --configuration Release --no-build --no-restore --filter-class '*LiveServerTests'
```

For an authenticated server, supply `MARMOTTE_MODEL_KEY` through the process's secret environment, not a committed file or a command containing the real key. The test never prints responses or keys. Routine CI skips it. This integration test expects discovery; controlled tests separately cover servers without it.

| Server/model | Recorded result, 2026-11-26 |
| --- | --- |
| LM Studio, installed application version `0.4.28+2`; `lfm3-1.7b`; host-loopback `/v1`; no authentication | Discovery, non-streaming text, streaming text, and client cancellation checked by the opt-in SDK test. Temperature 0 and `max_completion_tokens` accepted. |
| LM Studio with authentication enabled | Not tested on the real server; controlled Bearer/authentication-rejection tests only. |
| Image/vision capability | Unknown; no image was sent and no vision support is claimed. Preparation/transport remains M9. |
| Unsloth Desktop; `LFM3-1.7B-GGUF:Q8_0`; `http://127.0.0.1:8888/v1`; Bearer authentication | M8 verifies discovery, text, streaming and client cancellation with the SDK suite. Two users also exercise text probes, streaming chat, cancellation, reload and AppHost restart through the browser, reusing protected credentials. No server build fingerprint was captured. |

The reasoning model exhausted an initial 32-token probe before emitting visible text; the probe cap is now 256. The live cancellation case allows 1024 tokens so reasoning does not consume the entire answer budget. These limits do not imply a verified context window. The installed app version is read from its bundle; no server build fingerprint or model weight digest was captured. Host/container routing is described in [operations](operations.md); the model test runs on the host, not inside a container. M8 repeats LM Studio text/streaming/cancellation through the authenticated browser for two users, including reload and restart. Unsloth discovery returns `LFM3-1.7B-GGUF` while chat accepts the more specific manual identifier `LFM3-1.7B-GGUF:Q8_0`; discovery membership is not a prerequisite for generation. M6 task 6.8 remains open for vision and the remaining authentication matrix.
