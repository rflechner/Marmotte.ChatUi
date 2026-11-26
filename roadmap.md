# Roadmap — Marmotte.ChatUi

English | [Français](roadmap.fr.md)

> **Experimental POC — not production-ready.** Delivering the POC or a NuGet package does not change this status.

This roadmap turns the [architecture decisions](ARCHITECTURE.md) into actionable tasks, following the [contribution guidelines](AGENTS.md).

## Starting point

M1 through M5 are complete: solution foundations, the domain and authorized use cases, local Aspire infrastructure, OIDC identity with server-side session and user isolation, and PostgreSQL persistence with protected credentials. The M6 text adapter is implemented and verified with LM Studio. M7 delivers the reusable UI, themes and independent sample. Of the 82 tasks below, 49 are complete and 33 remain open. Full chat orchestration and NuGet publication remain planned; the remaining model compatibility matrix is tracked in 6.8.

## Scope and tracking

- Milestones M1 through M10 form the first POC delivery: chat, settings, identity, persistence, reusable components, and images for vision.
- M11 is a later iteration for PDFs; it does not block the first delivery.
- Each task has a stable identifier in `milestone.task` format, identical in both languages. Preserve existing identifiers when updating the plan.
- Check off a task only after its outcome is implemented and verified, with the relevant documentation updated in both languages.
- Dependencies identify the prerequisites for completing a milestone. Independent work can proceed in parallel once its prerequisites are available.
- This document commits to no dates or estimates.

## Overview

| Milestone | Outcome | Dependencies | Stage |
| --- | --- | --- | --- |
| [M1](#milestone-1) | Solution foundations | None | POC |
| [M2](#milestone-2) | Domain model and application ports | [M1](#milestone-1) | POC |
| [M3](#milestone-3) | Aspire, Keycloak, and PostgreSQL infrastructure | [M1](#milestone-1) | POC |
| [M4](#milestone-4) | OIDC identity and user isolation | [M2](#milestone-2), [M3](#milestone-3) | POC |
| [M5](#milestone-5) | PostgreSQL persistence and user settings | [M2](#milestone-2), [M3](#milestone-3), [M4](#milestone-4) | POC |
| [M6](#milestone-6) | OpenAI-compatible model adapter | [M2](#milestone-2), [M5](#milestone-5) | POC |
| [M7](#milestone-7) | Reusable UI and design system | [M1](#milestone-1), [M2](#milestone-2) | POC |
| [M8](#milestone-8) | Complete text-chat POC | [M4](#milestone-4), [M5](#milestone-5), [M6](#milestone-6), [M7](#milestone-7) | POC |
| [M9](#milestone-9) | Attachment uploads and image vision | [M8](#milestone-8) | POC |
| [M10](#milestone-10) | NuGet delivery and POC validation | [M9](#milestone-9) | POC |
| [M11](#milestone-11) | PDF preparation for vision — next iteration | [M10](#milestone-10) | After the first POC |

<a id="milestone-1"></a>

## Milestone 1 — Solution foundations

**Goal:** Create a buildable .NET solution with explicit assembly boundaries and a repeatable development workflow.

**Dependencies:** None.

- [x] **1.1** — Select compatible .NET, Aspire, and dependency versions; record prerequisites and pin the SDK and package versions.
- [x] **1.2** — Confirm the host application's Blazor rendering mode, starting from the server-interactivity proposal, and define the rendering modes supported by reusable components.
- [x] **1.3** — Create Domain, Contracts, Application, Components, Infrastructure.OpenAI, Persistence.PostgreSql, Web, AppHost, and ServiceDefaults with the intended dependency direction.
- [x] **1.4** — Configure nullable reference types, formatting, analyzers, and dependency management; document the C# and Razor conventions.
- [x] **1.5** — Set up the test projects and a CI workflow for restore, build, and relevant checks without requiring personal model servers or paid credentials.
- [x] **1.6** — Document the verified build and test commands in English and French, including how to configure development secrets.

**Exit criterion:** A fresh checkout builds and passes the initial checks; project references follow the architecture and the component library does not depend on server adapters.

**Validation (2026-11-25):** On macOS ARM64 with SDK 10.0.401, locked restore, Release build (zero warnings/errors), formatting, and all 18 tests passed from a source copy without existing build outputs. Direct web startup and web-only Aspire startup both served the foundation page and shut down successfully. Development-secret set/remove commands were checked with a dummy value. English/French document pairs and local links were checked. CI is configured for Ubuntu; no GitHub Actions run or cross-platform runtime validation is claimed. See the [development](docs/development.md), [operations](docs/operations.md), and [integration](docs/integration.md) guides.

<a id="milestone-2"></a>

## Milestone 2 — Domain model and application ports

**Goal:** Define the business behavior and contracts before binding them to UI or infrastructure.

**Dependencies:** [M1](#milestone-1).

- [x] **2.1** — Refine the vocabulary and aggregate boundaries for Conversation, Message, Attachment, ConnectionProfile, and UserPreferences.
- [x] **2.2** — Define identifiers, ownership, message ordering, timestamps, and the invariants required to create and resume a conversation.
- [x] **2.3** — Specify message generation states, including streaming, completion, cancellation, and failure, with rules for partial responses and duplicate submissions.
- [x] **2.4** — Define focused ports for conversation repositories, preferences, connection profiles, current identity, model capabilities, and response generation; keep provider types out of their signatures.
- [x] **2.5** — Implement the initial use cases for creating, listing, loading, and deleting conversations, recording messages, and managing preferences and profiles, with explicit authorization boundaries.
- [x] **2.6** — Define the presentation contracts exposed to the component library, including streaming updates, errors, cancellation, and attachment metadata.
- [x] **2.7** — Test business invariants, ownership checks, paging, and message lifecycle behavior through the application contracts.

**Exit criterion:** Core use cases and invariants can be exercised with test adapters, without Blazor, PostgreSQL, Keycloak, or the OpenAI SDK.

**Validation (2026-11-25):** On macOS ARM64 with SDK 10.0.401, locked restore, Release build (zero warnings/errors), formatting/analyzers, and all 51 tests pass, including 33 new core tests. Tests exercise ownership, paging, immutable/restored history, lifecycle and partial responses, duplicate submissions, concurrent writes, settings, and cancellation with test adapters. English/French pairs and local links were checked. No database, identity-provider, model-server, browser-chat, or NuGet validation is claimed. See the [core reference](docs/core.md) for API guarantees and remaining limitations.

<a id="milestone-3"></a>

## Milestone 3 — Aspire, Keycloak, and PostgreSQL infrastructure

**Goal:** Launch the local application infrastructure reproducibly, including a database with pgvector available.

**Dependencies:** [M1](#milestone-1).

- [x] **3.1** — Declare the web application, PostgreSQL, and Keycloak resources in AppHost, with explicit dependencies and readiness checks.
- [x] **3.2** — Select and pin a PostgreSQL container image that includes pgvector; configure persistent volumes and separate application and Keycloak databases.
- [x] **3.3** — Provide an idempotent process to enable the vector extension, and verify it on both an empty database and an existing volume.
- [x] **3.4** — Configure reproducible Keycloak realm and client initialization, persistent identity data, and browser-reachable authority and redirect addresses.
- [x] **3.5** — Inject connection information and secrets into the appropriate host services; keep resource configuration out of the domain and component package.
- [x] **3.6** — Configure ServiceDefaults, health reporting, and useful diagnostics without logging conversation contents, file contents, or credentials.
- [x] **3.7** — Document and verify local startup, shutdown, restart, and troubleshooting, including host/container access to locally running model servers.

**Exit criterion:** Aspire starts the required resources, reports their readiness, retains data across restarts, and makes the vector extension available in the application database.

**Validation (2026-11-26):** On macOS ARM64 with SDK 10.0.401 and Docker via Rancher Desktop, locked restore, Release build (zero warnings/errors), formatting/analyzers, 57 routine tests, and the opt-in Docker integration test pass. The actual AppHost with its dashboard starts three times on the same isolated volume: fresh initialization, idempotent restart, then repair after vector removal, preserving SQL probe data and a Keycloak user. Checks cover separate databases, vector 0.8.7, readiness, the browser-visible issuer, and confidential client/PKCE/redirect settings. Normal dashboard startup and Ctrl+C shutdown were also checked on the development volume. A temporary local HTTP probe confirmed host-loopback and container-to-host gateway access on Rancher Desktop. Both language versions and local links were reviewed. Seven platform-specific lock-file pairs are regenerated; runtime validation is limited to macOS ARM64. The Docker CI job is configured but no GitHub Actions run is claimed. OIDC sign-in, business migrations/repositories, and model compatibility remain for later milestones. See [operations](docs/operations.md) and [development](docs/development.md).

<a id="milestone-4"></a>

## Milestone 4 — OIDC identity and user isolation

**Goal:** Authenticate users of the full application and enforce ownership on the server.

**Dependencies:** [M2](#milestone-2), [M3](#milestone-3).

- [x] **4.1** — Integrate OIDC sign-in and sign-out with Keycloak in the web host, including session expiry and authentication failure handling.
- [x] **4.2** — Implement the current-user adapter and map stable application identity to the OIDC issuer/subject pair rather than email.
- [x] **4.3** — Protect application routes and server entry points, while keeping Keycloak and authentication implementation details out of the reusable UI package.
- [x] **4.4** — Enforce ownership in application operations and data access; reject browser-supplied owner identities as a source of authorization.
- [x] **4.5** — Keep application authentication separate from model-server credentials and prevent profile secrets from being exposed to other users.
- [x] **4.6** — Verify anonymous access, expired sessions, sign-out, and cross-user access denial; document local identity configuration and its production limitations.

**Exit criterion:** Two users can sign in independently, and authorization tests reject access to another user's data through the application contracts. Enforcement against actual database records and stored attachments is verified again in M5 and M9.

**Validation (2026-11-26):** On macOS ARM64 with SDK 10.0.401, locked restore, Release build (zero warnings/errors), formatting/analyzers, and all 78 routine tests pass. The Docker integration test also passes: real Keycloak forms authenticate two independent users, OIDC callbacks reach the protected workspace, antiforgery is enforced, RP-initiated logout clears Keycloak and local sessions, replayed cookies are rejected, and the other user stays signed in. The same test retains its three-start database/identity lifecycle checks. Routine tests exercise signed OIDC tokens, invalid issuer/audience/nonce/expiry, missing identity, protected routes/SignalR, fixed return destinations, expired/revoked circuit identities, remote failures, and cross-user conversation/profile/preference access with test repositories. English/French documents and local links were checked. Sessions are in-memory and single-process; Keycloak-initiated revocation is not implemented and real browser JavaScript/WebSocket behavior has not been exercised, as detailed in [operations](docs/operations.md) and [development](docs/development.md). Database/file enforcement remains M5/M9 work. No production deployment or GitHub Actions run is claimed.

<a id="milestone-5"></a>

## Milestone 5 — PostgreSQL persistence and user settings

**Goal:** Persist conversations and settings behind the application ports, entirely within the dedicated persistence adapter.

**Dependencies:** [M2](#milestone-2), [M3](#milestone-3), [M4](#milestone-4).

- [x] **5.1** — Use EF Core code first with generated migrations, keeping ORM and provider dependencies inside Persistence.PostgreSql.
- [x] **5.2** — Define mappings, keys, indexes, and migrations for conversations, messages, profiles, preferences, and attachment metadata, without speculative embedding tables.
- [x] **5.3** — Implement repository operations with user scoping, pagination, cancellation, and internal domain/DTO mapping; expose no database-specific types.
- [x] **5.4** — Define transaction and concurrency behavior for message ordering, partial generations, repeated submissions, and attachment references.
- [x] **5.5** — Persist connection profiles and UI preferences, including the theme and selected model/profile, with validated values.
- [x] **5.6** — Choose and implement protection and retrieval of model credentials separately from ordinary settings; exclude secrets from returned UI data and logs.
- [x] **5.7** — Implement the migration startup sequence and verify fresh initialization, upgrades of an existing database, and persistence across restarts.
- [x] **5.8** — Run PostgreSQL integration tests for repository behavior, ownership, ordering, concurrency, and deletion/retention rules.

**Exit criterion:** An authenticated user's history and settings survive an application restart, with migrations, paging, and isolation verified against PostgreSQL.

**Validation (2026-11-26):** On macOS ARM64 with SDK 10.0.401, locked restore, Release build (zero warnings/errors), formatting/analyzers, and all 84 routine tests pass, including EF model/snapshot agreement. The twelve opt-in PostgreSQL tests pass, including authenticated host/database restart, credential recovery and missing-key/tamper rejection, owner isolation, paging, concurrent revisions, deletion/retention, cancellation under a lock, upgrades between EF migrations, and adoption of both former SQL schemas with retained history/settings/secrets and drift rejection. The real Aspire test also passes its three-start pgvector/Keycloak/business-migration checks. The pinned EF CLI successfully lists migrations, checks the snapshot, updates a fresh database twice, and generates an idempotent script applied twice to a separate fresh database. CI includes the PostgreSQL suite and model-drift check; no GitHub Actions execution or production deployment is claimed. Whole-history writes, local unencrypted key files, and deferred UI/upload/recovery work are documented in [persistence](docs/persistence.md).

<a id="milestone-6"></a>

## Milestone 6 — OpenAI-compatible model adapter

**Goal:** Connect the application's internal chat contracts to LM Studio, Unsloth Desktop, and configurable compatible servers.

**Dependencies:** [M2](#milestone-2), [M5](#milestone-5).

- [x] **6.1** — Evaluate the OpenAI NuGet package against the required endpoints and credential behavior; confirm its adoption and version or document any blocking compatibility issue.
- [x] **6.2** — Implement configurable endpoints and translate SDK requests, responses, and exceptions to internal contracts inside Infrastructure.OpenAI.
- [x] **6.3** — Implement streaming, cancellation, timeouts, and partial-error handling; avoid automatic retries that duplicate a message or generation.
- [x] **6.4** — Implement connection checks and model discovery with manual model entry when discovery is unavailable.
- [x] **6.5** — Validate profile addresses, model identifiers, and options; support servers with and without authentication without requiring users to invent a key.
- [x] **6.6** — Represent verified server/model capabilities, especially vision, instead of assuming that every OpenAI-compatible endpoint supports all APIs.
- [x] **6.7** — Keep clients and credentials correctly scoped to their user/profile and document host/container endpoint configuration.
- [ ] **6.8** — Add adapter contract tests with controlled responses, then record actual text, streaming, authentication, discovery, and vision checks on LM Studio and Unsloth Desktop.

**Exit criterion:** The adapter exchanges messages with both target servers using configurable profiles, while the core and components remain independent of the SDK.

**M6 validation (2026-11-26):** Locked restore, Release build, formatting/analyzers, 138 routine tests (including 52 adapter tests), and bilingual document/link checks pass on macOS ARM64 with SDK 10.0.401. The thirteen Docker-dependent tests were not rerun for this change; the model test is opt-in and ran separately. OpenAI 2.15.0 is adopted behind internal ports, with scoped authorization/credentials, optional authentication, discovery/manual entry, explicit streaming checks, cancellation, total deadlines, and no retries. Controlled HTTP tests exercise the real SDK. The opt-in test passes against LM Studio (installed app `0.4.28+2`), `lfm3-1.7b`, `http://127.0.0.1:1234/v1`, without authentication: discovery, text, streaming, and client cancellation. Unsloth is deferred at the user’s request; real authenticated-server and vision checks are not claimed. Task 6.8 and the original two-server exit criterion remain open. See [models](docs/models.md) for the verified commands, capability evidence, SDK caveat, and compatibility matrix. No chat UI, generation persistence integration, NuGet publication, or production deployment is claimed.

<a id="milestone-7"></a>

## Milestone 7 — Reusable UI and design system

**Goal:** Build the reusable Blazor chat surface and its theming API independently of the complete application.

**Dependencies:** [M1](#milestone-1), [M2](#milestone-2).

- [x] **7.1** — Define semantic CSS tokens for surfaces, text, accents, borders, focus, states, spacing, typography, radii, and shadows.
- [x] **7.2** — Implement light, dark, and system themes, including explicit user selection and token overrides at application and widget scope.
- [x] **7.3** — Build reusable message-list, message, composer, generation-status, and attachment-preview components using presentation contracts.
- [x] **7.4** — Compose an embeddable chat widget with documented parameters, callbacks, and templates; avoid global configuration and server-circuit assumptions.
- [x] **7.5** — Handle empty, sending, streaming, cancellation, and error states, and render model-provided content safely.
- [x] **7.6** — Integrate the existing mascot into the host's visual identity while allowing consumers to replace or omit branding.
- [x] **7.7** — Verify responsive behavior, keyboard navigation, focus, labels, contrast, and accessible status announcements in each theme.
- [x] **7.8** — Add a minimal component sample and document integration, public APIs, assets, and token customization in both languages.

**Exit criterion:** A sample host renders the chat components with substituted services and theme tokens, without Keycloak, PostgreSQL, Aspire, or the OpenAI SDK.

**M7 validation (2026-11-26):** On macOS ARM64 with SDK 10.0.401, locked restore, Release build (zero warnings/errors), formatting/analyzers, and 162 routine .NET tests pass, including 20 component tests. Fourteen opt-in Docker/model tests were not rerun. Eleven Playwright tests pass against Chromium 151.0.7922.34: actual Interactive Server, keyboard/focus, encoded content, sending/streaming/completion, stopped and failed partial responses, draft retention, attachment blocking/removal, theme/OS changes, application and instance overrides, and 360px/1280px layouts. Axe reports no violations for the tested WCAG rule sets/screens; light desktop and dark mobile screenshots were inspected, and mascot delivery/provenance is checked. The sample starts independently with substituted responses and no server adapters. The public API, assets, token defaults, lifecycle ownership and limits are documented in [integration](docs/integration.md); both language versions and local links were checked. CI includes browser checks but no GitHub Actions run is claimed. Screen-reader usability, other browser engines, WebAssembly/Auto, full model/history orchestration, attachment transmission, and NuGet publication remain outside this validation.

<a id="milestone-8"></a>

## Milestone 8 — Complete text-chat POC

**Goal:** Assemble identity, persistence, model connections, and reusable components into a working chat application.

**Dependencies:** [M4](#milestone-4), [M5](#milestone-5), [M6](#milestone-6), [M7](#milestone-7).

- [ ] **8.1** — Compose services and adapters in the web host and wire the reusable components to application use cases.
- [ ] **8.2** — Build settings screens for connection profiles, endpoints, authentication, model selection, connection testing, and theme preferences.
- [ ] **8.3** — Implement conversation navigation, creation, loading, deletion, and paginated history scoped to the signed-in user.
- [ ] **8.4** — Connect message submission and streamed updates to persistence, including clear status for cancelled or interrupted generations.
- [ ] **8.5** — Handle unreachable servers, rejected authentication, unavailable models, context-limit errors, and dropped connections with actionable UI feedback.
- [ ] **8.6** — Use the existing banner/mascot and retain the experimental POC, not-production-ready notice in the application and documentation.
- [ ] **8.7** — Verify the full flow with two users and both target servers, including reload, restart, cancellation, and recovery from failures.

**Exit criterion:** A user can sign in, configure a local model, chat with streaming, stop a response, and reopen persisted conversations after a restart.

<a id="milestone-9"></a>

## Milestone 9 — Attachment uploads and image vision

**Goal:** Support image attachments through a bounded upload flow and send them only to compatible vision models.

**Dependencies:** [M8](#milestone-8).

- [ ] **9.1** — Choose the initial binary-storage adapter and retention policy behind an attachment-storage port; separate original files, prepared variants, and metadata.
- [ ] **9.2** — Implement authorized upload, retrieval, and deletion, with consistent database references and cleanup of abandoned temporary files.
- [ ] **9.3** — Unify file selection and drag and drop with previews, progress, removal before sending, cancellation, and clear errors.
- [ ] **9.4** — Validate actual image formats, file sizes, dimensions, and per-message totals on the server; make limits configurable.
- [ ] **9.5** — Prepare images with suitable dimensions and encoding while preserving readability and retaining the original when required by the storage policy.
- [ ] **9.6** — Map prepared images to the adapter's multimodal requests and enforce the selected model's verified capabilities; do not advertise PDF support before its processing exists.
- [ ] **9.7** — Test unsupported formats, oversized or invalid images, text-only models, cancellation, and cross-user attachment access.
- [ ] **9.8** — Verify image chat against supported models in LM Studio and Unsloth Desktop, and document observed limits and resource usage.

**Exit criterion:** A user can select or drop images, preview and remove them, send them to a verified vision model, and access persisted attachments only when authorized.

<a id="milestone-10"></a>

## Milestone 10 — NuGet delivery and POC validation

**Goal:** Make the first POC reproducible for evaluators and distribute the reusable components as an experimental package.

**Dependencies:** [M9](#milestone-9).

- [ ] **10.1** — Choose and verify the NuGet package identity and prerelease versioning; define how the public Contracts assembly is delivered so installing the component package is sufficient.
- [ ] **10.2** — Configure package metadata, licensing, documentation, symbols, styles, and static assets; verify that the package contains no secrets or server adapters.
- [ ] **10.3** — Pack and install the package in an independent sample application; verify custom services, token overrides, branding replacement, and supported rendering modes.
- [ ] **10.4** — Extend CI to exercise relevant unit, adapter, database, component, and package-consumption checks with controlled infrastructure.
- [ ] **10.5** — Prepare the release workflow and publish the first experimental NuGet prerelease only after packaging checks and tasks 10.6 through 10.8 are complete.
- [ ] **10.6** — Write verified startup, configuration, identity, migration, upload, integration, theming, and troubleshooting instructions in English and French.
- [ ] **10.7** — Record a compatibility matrix for the tested server/model versions and known limitations; keep the POC and not-production-ready notices visible.
- [ ] **10.8** — Run and document a clean-environment acceptance check: Aspire startup, OIDC, persistence, text/vision chat, user isolation, themes, and external NuGet consumption; update roadmap statuses from that evidence.

**Exit criterion:** The POC is documented and reproducibly validated, and an experimental component package is published and consumed by an independent sample. This milestone does not establish production readiness.

<a id="milestone-11"></a>

## Milestone 11 — PDF preparation for vision — next iteration

**Goal:** Prepare selected PDF pages for vision without unbounded memory usage or uncontrolled request size.

**Dependencies:** [M10](#milestone-10).

- [ ] **11.1** — Evaluate rendering libraries for platform/container support, licensing, resource usage, and output quality; record the selected engine and its deployment requirements.
- [ ] **11.2** — Introduce the document-preparation port and its dedicated adapter assembly without exposing renderer types to the domain or UI.
- [ ] **11.3** — Validate PDFs, inspect page counts, and handle corrupted or password-protected files before starting rendering.
- [ ] **11.4** — Render pages progressively with configurable page, pixel, dimension, memory, duration, and concurrency limits; support cancellation and temporary-file cleanup.
- [ ] **11.5** — Add page previews and selection while preserving order and page numbers; expose progress and explain when configured limits prevent processing.
- [ ] **11.6** — Resize and encode selected page images to balance text readability, transfer size, and model constraints; do not equate file size with context consumption.
- [ ] **11.7** — Evaluate text extraction for text-native PDFs and define when to use text, page images, or both; keep RAG and indexing outside this milestone.
- [ ] **11.8** — Connect prepared pages to the existing attachment and vision flows, with traceability to the original document and authorized storage access.
- [ ] **11.9** — Test scanned, text-native, large, malformed, and protected PDFs; verify readability and resource bounds with the target models, then update both language versions of the documentation.

**Exit criterion:** Scanned PDFs can be previewed and sent as selected, readable page images within configured limits; text PDFs, protected files, failures, and cancellation have explicit behavior.

## Outside the committed scope

RavenDB and other persistence providers are not part of this roadmap. Repository abstractions preserve the option to evolve without scheduling another adapter today.

RAG, document indexing, embedding generation, and vector search are not commitments of these milestones. pgvector is installed and enabled to prepare for a possible future addition, without building those features in advance.

Selecting a production target and completing the work needed to operate it will require a separate scope before any production deployment. This remains the roadmap for an experimental POC.
