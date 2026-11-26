# Domain model and application ports

English | [Français](core.fr.md)

## Implemented scope

M2 implements the domain, provider-neutral ports, presentation contracts, and authorized use cases for conversations and settings. [Core tests](../tests/Marmotte.ChatUi.Core.Tests) exercise them using an in-memory test adapter and a controlled clock. M4 registers the scoped OIDC `ICurrentUser` adapter in Web and tests it with these use cases. M5 composes these services with EF Core/PostgreSQL repositories when configured; EF entities and contexts stay inside the persistence adapter. M6 adds the [text model adapter](models.md); M8 adds [generation orchestration](chat.md), while uploads remain unimplemented. See [persistence](persistence.md) for schema and adapter behavior.

The AI model is the remote model selected in a connection profile. The domain model below describes application behavior and has no dependency on that provider, Blazor, or persistence libraries. Domain and Contracts have no project or package dependency; Application references only Domain and Contracts.

## Vocabulary and boundaries

| Type | Responsibility and invariants |
| --- | --- |
| `UserId` | Case-sensitive opaque application identity, 1–256 characters, not blank. The M4 identity adapter maps a trusted issuer/subject pair to this stable ID; a browser owner field is never accepted. |
| `Conversation` | Immutable aggregate root owning its complete ordered history. Nonempty GUID, immutable owner, title of 1–200 characters, UTC timestamps, positive revision. The repository saves the whole aggregate atomically. |
| `Message` | Immutable child of a conversation, with its own nonempty GUID, a submission GUID, one-based sequence, role, content, lifecycle, and UTC timestamps. Never saved independently of its conversation. |
| `Attachment` | Independently owned metadata referenced by a user message: GUID, owner, file name, media type, positive byte size, creation time, preparation state, optional positive width/height supplied together. No bytes, storage path, or public URL. |
| `ConnectionProfile` | Separate immutable user-owned settings aggregate: GUID, name (1–100 characters), endpoint, model (1–256 characters), optional generation options, and UTC timestamps. No API key or other secret. |
| `UserPreferences` | One immutable snapshot per user: System/Light/Dark theme, optional selected profile and model override. A model override requires a profile. |

All required strings reject whitespace-only values. GUIDs reject `Guid.Empty`. Creation and update times are passed explicitly, normalized to UTC, and must not run backwards. Application services obtain the time from injected `TimeProvider`. Constructors/factories reject invalid arguments with `ArgumentException` (including its derived types); invalid domain transitions throw `InvalidOperationException`. The application translates these to safe `InvalidRequest` and `Conflict` errors.

`Conversation.Create` starts at revision 1. `RecordMessage` and `UpdateResponse` return new snapshots with the next revision; existing instances and their read-only collections remain unchanged. `Restore` validates an entire persisted history: complete user/assistant pairs, distinct message/submission IDs, contiguous sequences, roles, chronological timestamps, minimum revision, attachment ownership, and at most one active response at the end. It is intended for persistence mapping, not browser input. Corrupt history must fail loading rather than silently losing/reordering data.

## Turns, retries, and generation

`RecordMessage(submissionId, content, now, attachments)` creates a completed user message and a pending assistant response together, sharing one submission ID and creation timestamp. User content must contain nonblank text or ready attachments. Attachment IDs must be unique within a message, belong to the same owner, and already exist at submission time. Metadata alone does not verify actual file bytes or model capabilities. The M2 application submission API accepts text only; upload validation, prepared variants, and vision authorization arrive in M9.

A conversation permits one pending/streaming response. It rejects another submission until that response reaches a terminal state. An identical retry with the same submission ID, text, and ordered attachment IDs returns the existing snapshot without writing, including after completion. Reusing that ID for different content conflicts. A concurrent retry can encounter a revision conflict; reload and retry the same submission ID. A new intentional turn always uses a new submission ID.

| Current response state | Allowed next states | Content behavior |
| --- | --- | --- |
| Pending | Streaming, Completed, Cancelled, Failed | Append the supplied delta; direct completion supports non-streaming providers. |
| Streaming | Streaming, Completed, Cancelled, Failed | Append in arrival order; preserve earlier text. |
| Completed, Cancelled, Failed | None | Immutable; reject late updates. A subsequent user turn is allowed. |

`Failed` requires a `GenerationFailure` category; other states prohibit one. Categories contain no raw provider messages. An empty pending response can be cancelled. Cancellation/failure preserves partial text for history. Restoring an active response does not resume a network request: `ChatService.LoadAsync` marks orphaned responses interrupted before a new turn. The single-process registry prevents recovering a response still running in another circuit.

Provider deltas are applied once in order and are **not idempotent**. Do not automatically retry an uncertain delta write. Repository revisions prevent lost concurrent updates but do not identify repeated deltas. On reconnect or ambiguous commit, reload persisted history; `ChatService` reloads uncertain commits without replaying a delta.

## Public application operations

Components depend only on interfaces and DTOs in `Marmotte.ChatUi.Contracts`. None of their requests accepts an owner ID. The Application implementations re-resolve `ICurrentUser` for each operation and verify ownership of returned records, including list results, in addition to the repository's mandatory owner filter.

| Service / method | Input and result |
| --- | --- |
| `IConversationService.CreateAsync` | Title → empty `ConversationView` with a server-generated ID. |
| `ListAsync` | `PageRequest` → `Page<ConversationSummary>` containing navigation metadata without message bodies. |
| `LoadAsync` | Conversation ID → complete ordered `ConversationView`. |
| `DeleteAsync` | Conversation ID → no value; load/authorize then delete with the observed revision. Missing/foreign IDs yield `NotFound`. |
| `RecordMessageAsync` | Conversation ID + `RecordMessageRequest(SubmissionId, Content)` → updated snapshot with user message and pending response. Does not invoke a model. |
| `CancelResponseAsync` | Conversation ID + assistant message ID → snapshot with durable cancellation and retained partial content. Terminal responses yield `Conflict`. |
| `ConversationService.RecordResponseAsync` | Server-only conversation/message IDs + `ResponseDelta` → snapshot after a provider update. Absent from the component interface; must not be exposed as a browser write endpoint. |
| `IConnectionProfileService.CreateAsync` / `UpdateAsync` | `SaveProfileRequest`, and ID for update → `ConnectionProfileView`; creation generates an ID, update preserves identity/creation time. |
| Profile `LoadAsync` / `ListAsync` / `DeleteAsync` | Owned ID or page → profile, page, or no value. Deletion also clears the owner's matching selected profile/model. |
| `IUserPreferencesService.LoadAsync` / `SaveAsync` | No input or `PreferencesView` → current preferences. Missing preferences default to System with no profile/model. Save verifies selected-profile ownership. |

All methods accept an optional `CancellationToken`. They throw `ChatOperationException` with `Unauthenticated` for missing identity, `NotFound` for absent or foreign resources, `InvalidRequest` for invalid input, and `Conflict` for lifecycle/revision conflicts. `PageRequest` itself throws `ArgumentOutOfRangeException` when constructed with invalid bounds. Provider failures use safe categories; presentation maps persisted failures to safe, actionable categories, ready for host-localized feedback. Unexpected adapter faults must be handled by the host without displaying raw exception details.

Cancelling an I/O token throws `OperationCanceledException`; this does not persist a cancelled response. Use `CancelResponseAsync` with a live operation token for that. Cancellation after a storage commit can leave a successful write; reload before retrying. No automatic write retry is implemented.

Profiles require an absolute HTTP(S) endpoint without user info, query, or fragment. Paths and explicit ports are preserved; no `/v1`, port, key, or provider version is added. Temperature is optional, finite, and nonnegative; maximum output tokens are optional and positive. Exact parameter ranges, endpoint reachability, network access policy, capabilities, and authentication requirements remain adapter/host responsibilities; M6 implements temperature 0–2 and positive output limits, with optional fields omitted. M5 adds server-only `ModelCredentialService` and `IModelCredentialStore`, separate from component contracts: save, remove, and resolve credentials using the current trusted identity. Missing/foreign profiles yield `NotFound`; a missing credential on an owned profile returns null. Credential validation rejects blank values, control characters, and values longer than 8192 characters. Storage and cryptographic failures use the safe `StorageUnavailable` category.

## Port guarantees and composition

| Port | Required adapter behavior |
| --- | --- |
| `ICurrentUser` | Resolve trusted current identity or null for anonymous/expired identity; never cache across users. M4 supplies OIDC mapping and checks active server sessions on every call. |
| `IConversationRepository` | Owner-scoped find/list/save/delete. Save uses compare-and-swap against `expectedRevision`; 0 means insert, otherwise write exactly the next revision. Atomically store the turn, ordering, metadata, and authorized attachment references. Reject stale revisions without a partial write. Delete removes message references atomically; binary retention remains separate. |
| `IConnectionProfileRepository` | Owner-scoped find/list/insert/update/delete; distinguish insert from update to prevent accidental recreation. Delete atomically clears matching preferences. No secrets in these records. |
| `IUserPreferencesRepository` | Owned upsert with one snapshot per user; validate selected-profile ownership/existence in the same transaction to close races with profile deletion. |
| `IAttachmentMetadataRepository` | Insert/read owned immutable metadata; no bytes or upload validation. Conversations reference existing ready metadata; deletion retains it for the future upload lifecycle. |
| `IModelCredentialStore` | Protected credential storage scoped to an owned profile, separate from settings. Profile deletion also deletes its credential. |
| `IModelCapabilities` | Capabilities for the exact authorized profile/model: Unknown, Unsupported, or Supported, independently for streaming and image input. Unknown never means supported. |
| `IModelConnection` | Explicit synthetic text/streaming checks and optional model discovery. Discovery failure does not prohibit manual entry. |
| `IResponseGenerator` | Ordered `IAsyncEnumerable<ResponseDelta>` for an authorized `ResponseRequest(Profile, History, Stream = false)`. History ends at the new user message, excluding its pending assistant placeholder. Caller explicitly chooses whether/how to include earlier partial responses. |

Repository Find returns null for missing/foreign data; write/delete errors do not reveal another user's record. Credential Find instead requires an existing owned profile (`NotFound` otherwise), returning null only when that profile has no credential. Repositories return detached immutable domain snapshots. Conversation writes use optimistic concurrency; settings use last committed write wins. List operations use ascending `CreatedAt`, then ascending `Id`, with a zero-based offset and limit of 1–100 (default 25). Fetch one extra record to set `HasMore`; no total count or cross-request snapshot is promised. Concurrent inserts/deletes can move offset pages.

Response adapters must validate options/capabilities, emit streaming deltas and exactly one terminal result, map provider failures without leaking SDK exceptions, impose a documented finite timeout, propagate cancellation during enumeration, and release transport resources on early disposal. M6 implements these guarantees with a default two-minute deadline; see [model adapters](models.md) for scoped evidence, errors, and measured compatibility. Attachment storage/preparation ports will be introduced with their actual use cases in M9/M11.

Compose the three services manually or in the host's DI container with trusted identity, repository adapters, and `TimeProvider.System`. Services do not own or dispose injected dependencies; scope them with the current request/user and do not share identity-bearing adapters as singletons. Domain snapshots are immutable; adapters manage their own resource lifetimes. Test adapters remain under tests and are not an application persistence implementation.

## Presentation and current limitations

`ConversationView`, `MessageView`, `AttachmentView`, `ConnectionProfileView`, and `PreferencesView` carry no owner ID, secret, SDK type, or storage address. `MessageView` exposes sequence, timestamps, submission ID, lifecycle, partial content, and safe error category. `GenerationUpdate` defines the future incremental UI event (conversation/message ID, delta, state, optional error); M8 uses ordered full `ConversationView` snapshots instead. Consumers apply updates once in order and reload snapshots on reconnect. Attachment status describes preparation, not proof of sending to a compatible model.

Conversation list pages avoid message bodies, but loading and updating a conversation currently materializes its complete history. This deliberately small initial aggregate is not an optimized solution for unbounded histories or token-by-token database writes. M5 verifies transactions and loading against PostgreSQL; M8 implements checkpointing, recovery and the connected chat UI. M6 implements model calls and streaming; uploads and NuGet delivery remain in later milestones. User authentication is implemented in M4.

Run the solution checks in the [development guide](development.md). Tests cover ownership (including faulty read adapters), paging, settings validation/selection/deletion, cancellation, duplicate submissions, concurrent writes, immutable snapshots, restoration, attachment metadata invariants, and response lifecycle without external infrastructure. Opt-in M5 tests additionally verify PostgreSQL behavior and authenticated restarts; M6 adapter checks and their limited server matrix are documented separately in [models](models.md).
