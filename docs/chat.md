# Text chat workspace

English | [Français](chat.fr.md)

M8 connects the authenticated Web workspace to PostgreSQL and the text model adapter. It uses the same reusable components as the independent sample. This remains an **experimental POC, not production-ready**; attachments and vision are M9 work.

## Using the workspace

1. Start Aspire as described in [operations](operations.md), create a Keycloak user and sign in. The standalone host without PostgreSQL still serves its landing page; the workspace explains that chat storage needs configuration.
2. Open **Connection settings**, enter a profile name, the full API base URL reachable from Web, and the exact model identifier. Do not add a version path or port unless your server uses it. Optional temperature is 0–2; maximum output tokens must be positive.
3. Choose **Set or replace API key** only when your model server requires authentication. Leaving the default preserves an existing key; a new profile starts without one. **Remove saved key** switches to anonymous requests. Saved secrets are never returned to the browser. The key field is cleared after an operation.
4. Save. **Discover models** offers identifiers when supported; manual model entry remains available. Save the selected model before testing. **Test text connection** and **Test streaming** send a small fixed prompt using the saved settings. They can consume local inference resources and have a cancel button.
5. Create a named conversation or open one from the paginated history. Select the connection and send text. **Stream response** is opt-in; a successful streaming probe enables it. Evidence lasts five minutes in the current circuit and applies only to the exact saved profile/model/options. After reload, profile changes or expiry, test streaming again or turn streaming off. No synthetic probe runs automatically when sending a message.
6. **Stop response** cancels inference and waits for cleanup. Partial text remains visible. **Reload conversation** reads durable history and recovers orphaned responses. Conversation and profile deletion require an explicit in-page confirmation. Profile deletion also removes its credential and clears the selected profile.

Theme and connection selection persist per user. The UI uses the model saved in the profile and clears a separate model override when selecting/saving a profile. Connection/profile pages contain at most 20 records per request; history uses Previous/Next and connections use Load more. Navigation waits for the current response to finish or stop. Unsaved drafts and form edits are local to the current component and are not restored after a full reload.

Settings and credentials are separate writes. If the credential write fails after settings were saved, the UI reports that the profile settings were saved and offers a safe error. Re-enter the key and save again; the existing profile ID is retained. No stored secret is read back for editing.

## Generation and persistence

`ChatService` is a user-scoped application orchestrator, independent of Blazor and provider/database libraries. `SendAsync` accepts an owned conversation ID, an owned profile ID, a stable `RecordMessageRequest` and an explicit streaming choice. It authorizes both resources before writing the user message/pending response pair. The first yielded snapshot confirms durable acceptance; the host clears the draft then observes the rest of the iterator. It retains the submission ID when acceptance is uncertain. An identical retry returns existing history without invoking the model again; a different use of the same ID conflicts.

The model receives only completed previous user/assistant pairs and the new user message. Cancelled and failed pairs remain visible in history but are excluded from subsequent model context. The pending placeholder is never sent. No automatic request retry, regeneration or silent fallback from streaming is implemented.

Ordered provider deltas update the screen. Persistence combines text into a checkpoint after 250ms when another delta arrives, or after 1024 characters. A terminal result always flushes the buffer, including cancellation. Each checkpoint is one atomic optimistic-revision write; immutable aggregate revisions do not advance once per displayed token. A hard crash can lose the displayed text since the last checkpoint. Loading/writing the entire aggregate still makes very long histories expensive; this is not an unbounded-history design.

A failed/ambiguous write is never replayed. Cleanup reloads durable history, preserves any committed text and terminates the active response without duplicating an uncertain delta. Cleanup gets its own five-second deadline and rechecks the current identity. If storage is unavailable or the session has expired, it cannot promise a terminal write. After storage/session recovery, opening the conversation marks the orphaned response **interrupted**, preserving committed text. It never resumes a remote request automatically.

`GenerationRegistry` is a singleton holding only temporary reservations/cancellation signals, keyed by trusted owner and conversation. It prevents competing sends, recovery and deletion across circuits. Loading a conversation being generated elsewhere returns its current persisted snapshot; **Reload conversation** refreshes it. Stopping from another circuit waits for the owner iterator to release its reservation. Every iterator must be consumed or disposed. Web keeps its generation task, observes faults and cancels/awaits it on component disposal. A temporary SignalR disconnect can leave a retained circuit generating; reconnect can resume that circuit, while a new circuit reloads durable state. This is not a durable background job service.

**One Web process per database is supported.** The registry is not a distributed lease. Do not run multiple Web instances against the same database with this recovery policy. Session storage already has the same single-process restriction. Process restart clears reservations and sessions; the next authenticated load recovers any persisted active response.

## Errors and boundaries

Safe categories retain useful actions without raw server bodies or exception details: unavailable/timeout (check/start the endpoint), rejected request (verify key, model name, loaded model and supported options), context/output limit (shorten history or adjust output limit), and interruption (retain saved partial output and explicitly send a new message). The adapter groups HTTP authentication and unavailable-model rejections; it does not infer exact causes from free-form provider error text. Other errors include expired authentication, storage unavailability, missing resources and revision conflicts. Connection testing/discovery uses the same authorized profile and protected credential path as generation.

Application services derive ownership from current trusted identity, recheck returned records and revalidate identity at each checkpoint. `ModelConnectionService` resolves an owned stored profile for tests/discovery. The host owns composition and current-user scoping. Components still reference only Contracts; the new optional `ChatWidget.BlockedReason` prevents submission while configuration/selection is incomplete.

## Verification

The core suite covers acceptance-before-inference, idempotency, cancelled/failed partial text, checkpoint batching, ambiguous commits, invalid capabilities, foreign profiles/history, expired identity, missing terminal events, early disposal, competing circuits and recovery. Routine tests require no Docker or paid API.

The browser driver `tests/browser/workspace.mjs` is invoked by the actual Aspire integration test with isolated volumes, temporary Keycloak users and a controlled HTTP model server. It checks two users, protected credentials, discovery, both response modes, cancellation, error recovery, paging, deletion, reload, full AppHost restart, responsive layouts and axe checks. Install browser dependencies as in [development](development.md), then run:

```sh
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 MARMOTTE_RUN_BROWSER_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Infrastructure.Tests --configuration Release --no-build --no-restore
```

Optional environment variables add real model checks to that same browser flow: `MARMOTTE_MODEL_ENDPOINT`, `MARMOTTE_MODEL_ID`, optional `MARMOTTE_MODEL_KEY` for LM Studio; `MARMOTTE_UNSLOTH_ENDPOINT`, `MARMOTTE_UNSLOTH_MODEL`, optional `MARMOTTE_UNSLOTH_KEY` for Unsloth. Inject secrets privately; never commit them. CI runs only the controlled server. Screenshots use synthetic test content and live under ignored `tests/browser/test-results/`. The [roadmap](../roadmap.md#milestone-8) records completed validation; [models](models.md) distinguishes actual compatibility from planned vision support.
