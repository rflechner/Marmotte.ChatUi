# PostgreSQL persistence

English | [Français](persistence.fr.md)

## Scope and approach

M5 implements the application repositories in `Marmotte.ChatUi.Persistence.PostgreSql` with Npgsql 10.0.0 and parameterized SQL. Explicit SQL keeps owner filters, revision comparisons, locking, and migrations reviewable without introducing an ORM or database entities into the domain. [Npgsql data sources](https://www.npgsql.org/doc/basic-usage.html) provide the singleton connection pool; repositories lease a connection per operation, including in Blazor circuits. No connection, reader, transaction, or provider type crosses the adapter's public API.

`AddPostgreSqlPersistence` registers the pool and `PostgreSqlMigrations` as singletons and the repositories as scoped services. DI owns disposal. Web composes the conversation, profile, preference, and credential services when `ConnectionStrings:chatdb` exists. Without it, standalone Web retains its foundation-page mode. The settings screens and model calls remain M8/M6 work.

## Schema and consistency

All application tables live in schema `chat`. The numbered embedded SQL files are the schema source; `chat.schema_migrations` stores their names, SHA-256 checksums, and application timestamps.

| Table | Keys, indexes, and retention |
| --- | --- |
| `conversations` | Global UUID, owner/UUID unique key, positive revision, owner/creation/UUID paging index. Deletion cascades to messages. |
| `messages` | Owner/conversation/message key; unique sequence and submission/role per conversation; one active assistant response. Deletion cascades to attachment references. |
| `attachments` | Global UUID and owner/UUID key, validated metadata, no bytes or storage paths. Retained when conversations are deleted. |
| `message_attachments` | Owner/conversation/message/attachment key, ordered positions, composite owner foreign keys to messages and metadata. |
| `profiles` | Global UUID, owner/UUID unique key, owner/creation/UUID paging index. Ordinary settings only. |
| `preferences` | One row per owner, theme, optional owned profile and model override. |
| `profile_credentials` | Owner/profile key, protected bytes, cascade deletion with the profile. |

All reads and writes filter by the trusted server owner. Composite foreign keys additionally prevent references across owners. IDs supplied by the browser are never ownership evidence. Loading restores domain objects through their validation constructors; corrupt histories fail rather than being silently repaired. PostgreSQL timestamps have microsecond precision, so submicrosecond .NET ticks are truncated on storage.

Conversation saves compare the expected revision and write exactly its successor, atomically replacing the message/reference rows. Competing writes have one winner; stale updates and deletes yield `Conflict`. A repeatable-read transaction loads metadata, messages, and attachment references from one consistent snapshot. Navigation selects no message bodies, orders by creation time then UUID, and fetches one extra row for `HasMore`. Offset pages are not snapshots across requests.

An identical submission retry is recognized by the application/domain after reload; deltas are not idempotent. Partial text, generation state, and safe failure categories survive restart. A restart does not resume a model call or automatically mark an active response interrupted. M8 must provide recovery and sensible checkpoint frequency. Whole-history replacement is intentionally simple and proportional to history size; it is not suitable for unbounded histories or persisting every token.

Profile and preference updates use last committed write wins. Profile selection, credential writes, and profile deletion first lock the owned profile row. Deletion clears the matching profile/model selection and removes the encrypted credential in the same transaction, preserving the theme. A concurrently deleted selection cannot be recreated by a late save.

`IAttachmentMetadataRepository` inserts and reads immutable metadata for trusted server preparation. Referencing a file requires existing, matching, ready metadata owned by the conversation's user, within the same transaction. Missing/foreign/changed metadata rolls back the conversation revision and messages. No upload endpoint, binary access, preparation transition, orphan cleanup, or retention duration is implemented; these belong to M9.

## Credential protection

`IModelCredentialStore` is separate from ordinary settings. `ModelCredentialService` resolves the current identity on every call: `SaveAsync` replaces a validated secret, `DeleteAsync` removes it, and `ResolveAsync` returns it only to server-side transport. Null means an owned profile has no credential; missing/foreign profiles yield `NotFound`. None of these values is part of a component DTO. Do not expose credential retrieval as a browser endpoint. `ModelCredential.ToString()` is redacted; its `Value` must never be logged or serialized.

ASP.NET Core Data Protection encrypts and authenticates the bytes before SQL execution. The purpose chain includes a version, the exact owner, and the profile UUID, so copied or modified ciphertext cannot be decrypted for a different profile/user. Keys stay outside PostgreSQL. Missing keys and corrupt ciphertext fail closed with `StorageUnavailable`; they never become an empty credential. Managed plaintext strings still exist briefly in server memory.

Web uses application name `Marmotte.ChatUi` and `Persistence:KeyDirectory`, defaulting to `Environment.SpecialFolder.LocalApplicationData/Marmotte.ChatUi/keys`. The setting must be an absolute path. Aspire forwards an optional `Infrastructure:KeyDirectory` to Web; tests supply private temporary directories. Newly created Unix directories use owner-only access. Protect existing directories and Windows ACLs yourself. These local POC keys are not encrypted at rest by this configuration. See [Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Back up the database **and** its separate key directory. Keep the application name and old keys stable across deployments/restores; automatic key rotation retains old keys for decryption. Losing keys requires users to replace their model credentials. A production key vault/certificate, shared-instance key storage, and a credential re-encryption procedure are outside this POC. OIDC tokens and the in-memory login session store remain separate from model credentials; Web restart still requires sign-in again.

## Startup, upgrades, and errors

Aspire waits for PostgreSQL and successful pgvector activation before starting Web. Web's hosted startup service applies migrations before accepting requests, with a two-minute overall deadline. A transaction-level advisory lock serializes migrators. All pending migrations and history entries commit together; failure rolls back. Existing checksums, unknown versions, and gaps in applied history are rejected. Never edit an applied migration; add the next numbered file. No automatic downgrade or destructive reset exists. Back up before an upgrade; test restoration separately before a production deployment.

Connection acquisition is bounded to 15 seconds, commands/server statements to 30 seconds, and lock waits to 10 seconds. Cancellation reaches every database operation. Constraint failures map to safe `Conflict`, `NotFound`, or `InvalidRequest`; other database failures map to `StorageUnavailable`, without raw SQL exception details. No write is retried automatically. Cancellation or connection loss around commit can have an ambiguous outcome: reload before retrying. The adapter disables parameter logging and detailed server errors and does not enable SQL tracing.

The application migrations do not install pgvector: the existing Aspire initializer handles activation, independently of the business schema. Standalone operators must provision the database and extension themselves. No embeddings or vector-search tables are created. Runtime health endpoints still report process health, not ongoing database reachability.

## Verification

After the [development checks](development.md), with Docker running:

```sh
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Web.Tests --configuration Release --no-build --no-restore
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Infrastructure.Tests --configuration Release --no-build --no-restore
```

The Web suite creates an isolated pinned PostgreSQL container and key directory, then removes them. It covers actual OIDC users with PostgreSQL repositories, host/database restart and secret recovery, paging, concurrent writes, cancellation under a row lock, attachment reference rollback/retention, preference/deletion races, ciphertext tampering and purpose isolation, upgrades from schema 001, concurrent migration startup, and checksum drift. The Aspire suite checks the complete startup sequence, pgvector activation, migration history, and existing-volume restarts. Normal runs skip Docker tests; no personal model server or paid key is required.
