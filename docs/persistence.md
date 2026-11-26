# PostgreSQL persistence

English | [Français](persistence.fr.md)

## Scope and approach

M5 implements the application repositories in `Marmotte.ChatUi.Persistence.PostgreSql` with **EF Core 10.0.12 code first** and the [Npgsql EF provider](https://www.npgsql.org/efcore/) 10.0.0. Internal persistence entities and Fluent API configurations define the relational model; generated C# migrations and a model snapshot version it. Domain objects remain independent of EF. No context, entity, queryable, connection, transaction, or provider type crosses the adapter's public API. LINQ, tracked writes, and EF bulk updates/deletes implement repository operations. Parameterized SQL through EF is limited to PostgreSQL row/advisory locks and the one-time legacy-schema bridge.

`AddPostgreSqlPersistence` registers a singleton `IDbContextFactory<ChatDbContext>` and `PostgreSqlMigrations`, with scoped repositories. Each operation creates and disposes its own context, avoiding a shared change tracker across a Blazor circuit. Npgsql pools the underlying connections. Web composes the conversation, profile, preference, and credential services when `ConnectionStrings:chatdb` exists. Without it, standalone Web retains its foundation-page mode. M8 connects settings screens and model calls; see [chat](chat.md).

## Schema and consistency

All application tables live in schema `chat`. `ChatDbContext` and `EntityConfigurations.cs` define mappings, keys, indexes, checks, and relationships. The `Migrations` directory contains `InitialHistoryAndSettings`, `ProtectedModelCredentials`, their generated metadata, and `ChatDbContextModelSnapshot`. EF records applied identifiers and product versions in `chat."__EFMigrationsHistory"`. Neither `EnsureCreated` nor a hand-maintained SQL schema is used.

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

An identical submission retry is recognized by the application/domain after reload; deltas are not idempotent. Partial text, generation state, and safe failure categories survive restart. A restart does not resume a model call or automatically mark an active response interrupted. `ChatService.LoadAsync` now recovers orphaned active responses on authenticated access, and M8 batches generation checkpoints; see [chat](chat.md). Whole-history replacement is intentionally simple and proportional to history size; it is not suitable for unbounded histories or persisting every token.

Profile and preference updates use last committed write wins. A transaction-scoped owner lock serializes preference upserts, including simultaneous first saves. Profile selection, credential writes, and profile deletion first lock the owned profile row. Deletion clears the matching profile/model selection and removes the encrypted credential in the same transaction, preserving the theme. A concurrently deleted selection cannot be recreated by a late save.

`IAttachmentMetadataRepository` inserts and reads immutable metadata for trusted server preparation. Referencing a file requires existing, matching, ready metadata owned by the conversation's user, within the same transaction. Missing/foreign/changed metadata rolls back the conversation revision and messages. No upload endpoint, binary access, preparation transition, orphan cleanup, or retention duration is implemented; these belong to M9.

## Credential protection

`IModelCredentialStore` is separate from ordinary settings. `ModelCredentialService` resolves the current identity on every call: `SaveAsync` replaces a validated secret, `DeleteAsync` removes it, and `ResolveAsync` returns it only to server-side transport. Null means an owned profile has no credential; missing/foreign profiles yield `NotFound`. None of these values is part of a component DTO. Do not expose credential retrieval as a browser endpoint. `ModelCredential.ToString()` is redacted; its `Value` must never be logged or serialized.

ASP.NET Core Data Protection encrypts and authenticates the bytes before assigning the EF entity; no plaintext credential is tracked. The purpose chain includes a version, the exact owner, and the profile UUID, so copied or modified ciphertext cannot be decrypted for a different profile/user. Keys stay outside PostgreSQL. Missing keys and corrupt ciphertext fail closed with `StorageUnavailable`; they never become an empty credential. Managed plaintext strings still exist briefly in server memory.

Web uses application name `Marmotte.ChatUi` and `Persistence:KeyDirectory`, defaulting to `Environment.SpecialFolder.LocalApplicationData/Marmotte.ChatUi/keys`. The setting must be an absolute path. Aspire forwards an optional `Infrastructure:KeyDirectory` to Web; tests supply private temporary directories. Newly created Unix directories use owner-only access. Protect existing directories and Windows ACLs yourself. These local POC keys are not encrypted at rest by this configuration. See [Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Back up the database **and** its separate key directory. Keep the application name and old keys stable across deployments/restores; automatic key rotation retains old keys for decryption. Losing keys requires users to replace their model credentials. A production key vault/certificate, shared-instance key storage, and a credential re-encryption procedure are outside this POC. OIDC tokens and the in-memory login session store remain separate from model credentials; Web restart still requires sign-in again.

## Startup, upgrades, and errors

Aspire waits for PostgreSQL and successful pgvector activation before starting Web. Web's hosted startup service calls `PostgreSqlMigrations.ApplyAsync`, then EF `Database.MigrateAsync`, before accepting requests, with a two-minute overall deadline. EF manages migration locking, transactions, and history. Transaction boundaries follow EF and the generated operations, including any operation explicitly excluded from a transaction; investigate a failed upgrade before restarting. The startup wrapper rejects unknown identifiers or gaps in EF history. Never edit an applied migration: change the model and generate the next migration. No automatic downgrade or destructive reset exists. Back up before an upgrade; test restoration separately before a production deployment.

Connection acquisition is bounded to 15 seconds, commands/server statements to 30 seconds, and lock waits to 10 seconds. Cancellation reaches every database operation. Constraint failures map to safe `Conflict`, `NotFound`, or `InvalidRequest`; other database failures map to `StorageUnavailable`, without raw SQL exception details. No write is retried automatically. Cancellation or connection loss around commit can have an ambiguous outcome: reload before retrying. The adapter disables parameter logging and detailed server errors and disables EF logging to prevent private statement data from reaching host log providers.

The application migrations do not install pgvector: the existing Aspire initializer handles activation, independently of the business schema. Standalone operators must provision the database and extension themselves. No embeddings or vector-search tables are created. Runtime health endpoints still report process health, not ongoing database reachability.

## EF migration workflow

Run these commands from the repository root. The local tool manifest pins `dotnet-ef` to 10.0.12. The design-time factory reads only `ConnectionStrings__chatdb`; it does not start Web, Aspire, or Keycloak. A non-secret dummy connection is sufficient for listing, scaffolding, scripting, and model checks:

```sh
dotnet tool restore
export ConnectionStrings__chatdb='Host=localhost;Database=marmotte_design;Username=design'
dotnet ef migrations list --no-connect --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
dotnet ef migrations script --idempotent --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release --output artifacts/postgresql-migrations.sql
```

For a schema change, edit the internal entities/configurations, then run:

```sh
dotnet ef migrations add DescribeSchemaChange --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
```

Replace `DescribeSchemaChange` with a meaningful name. Review `Up`, `Down`, generated metadata, and the snapshot together; commit all of them. Normalize generated code to the repository style: internal types, XML summaries, file-scoped namespaces, and analyzer-compliant collections. Rebuild and verify no pending model changes. Test both a fresh database and an upgrade with existing data; an empty diff check alone proves no runtime behavior. Removing a migration is only appropriate before it has been applied/shared; never delete history to bypass a failed upgrade.

For a fresh or already EF-managed database, supply its real connection through a private environment variable, then run:

```sh
dotnet ef database update --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
```

Do not put credentials in command arguments, committed settings, or shell history. The operator must provision the database/extension and grant the schema changes needed by migrations. The local POC automatically migrates at startup; production would need a reviewed deployment procedure. Review generated scripts before applying them to a matching schema. See [EF migration management](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing) and [deployment](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

## Existing SQL-managed databases

Databases from the earlier M5 implementation retain `chat.schema_migrations`. On the **first Web startup**, a compatibility bridge checks the two known SQL checksums, mapped column shapes, and constraint names under the previous advisory lock. It creates the missing preference foreign-key index and records the equivalent EF baseline atomically, without replacing user tables. EF then applies any remaining migration. A version-001 database receives the credential migration; a version-002 database retains its encrypted credentials and existing key directory. The old ledger is retained as historical evidence; EF owns subsequent changes.

An unknown, edited, or incomplete legacy schema fails startup instead of guessing or resetting data. The checks detect missing/changed columns and constraint names; they are not a complete audit of constraint expressions or indexes. Resolve schema drift from a backup/reviewed repair. **Do not run `dotnet ef database update` or a generated initial script against an unadopted legacy database**: those commands bypass the host's compatibility bridge. Start the new Web version once first, then use EF tooling. Historical SQL files remain only as test fixtures, never as runtime migrations.

## Verification

After the [development checks](development.md), with Docker running:

```sh
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Web.Tests --configuration Release --no-build --no-restore
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Infrastructure.Tests --configuration Release --no-build --no-restore
```

The Web suite creates an isolated pinned PostgreSQL container and key directory, then removes them. It covers actual OIDC users with PostgreSQL repositories, host/database restart and secret recovery, paging, concurrent writes, cancellation under a row lock, attachment reference rollback/retention, preference/deletion races, ciphertext tampering and purpose isolation, upgrades between EF migrations, concurrent migration startup, adoption of both legacy SQL versions with data retention, incompatible history/schema rejection, and model/snapshot drift. The Aspire suite checks the complete startup sequence, pgvector activation, migration history, and existing-volume restarts. Normal runs skip Docker tests; no personal model server or paid key is required.
