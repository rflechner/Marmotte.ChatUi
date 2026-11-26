# Operations

English | [Français](operations.fr.md)

## Implemented scope

M3 supplies local Aspire orchestration, PostgreSQL with pgvector, a persistent Keycloak realm/client, and Web health/telemetry defaults. M4 adds OIDC sign-in/sign-out, a protected workspace, and server session/ownership checks. Business repositories/migrations (M5), model calls (M6), and uploads remain unimplemented. No production deployment is configured.

## Local startup and shutdown

Install the SDK selected by [global.json](../global.json), run a Docker-compatible container engine, and allow access to NuGet, Docker Hub, and Quay for the first downloads. Aspire uses the SDK's dashboard/orchestrator packages, without a separate CLI. Ensure the development HTTPS certificate is available; see [.NET development certificates](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).

From the repository root:

```sh
dotnet restore Marmotte.ChatUi.slnx --locked-mode
dotnet build Marmotte.ChatUi.slnx --configuration Release --no-restore
dotnet run --project src/Marmotte.ChatUi.AppHost --configuration Release --no-build
```

Open the authenticated dashboard link printed in the terminal; its login token is private. The dashboard uses `https://localhost:17291`, OTLP uses `21291`, and the resource service uses `22291`. These launch-profile ports are local development choices, not model-server defaults.

Expected resources:

| Resource | Readiness and role |
| --- | --- |
| `postgres` | PostgreSQL connection health; a named data volume survives container removal |
| `pgadmin` | Local web administration, preconfigured for `postgres`; waits for PostgreSQL and checks `/browser` |
| `chatdb` | Database `marmotte_chat`, created if missing and checked for connectivity |
| `keycloakdb` | Separate database `marmotte_identity`, created if missing and checked for connectivity |
| `database-init` | Waits for `chatdb`, runs extension activation, then exits with code 0; an exited initializer is expected |
| `keycloak` | Waits for `keycloakdb`, imports the realm if missing, then passes its management `/health/ready` check |
| `web` | Waits for successful initialization and Keycloak readiness, then passes `/health` |

The [web host](http://localhost:5180) and [Keycloak admin console](http://localhost:8180/admin/) are browser-reachable. Web provides a sign-in link and a protected `/workspace` with a sign-out form. Ctrl+C in the AppHost terminal stops its processes and containers, retaining the database volume. Run the same command again to resume. Do not run two AppHosts against the same volume, or the standalone host and AppHost against the same web port.

For UI/core work without Docker:

```sh
dotnet run --project src/Marmotte.ChatUi.Web --configuration Release --no-build
```

Without authentication settings, the standalone host displays its public page and keeps protected routes inaccessible. Partial OIDC configuration fails at startup. `/health` and `/alive` return `Healthy` in Development; no external dependency is claimed by those process checks.

## pgAdmin in the browser

Open [pgAdmin](http://localhost:5050) after starting AppHost, or select the `pgadmin` endpoint in the Aspire dashboard. Expand `postgres`, then `Databases`, to inspect `marmotte_chat` and `marmotte_identity`. Aspire registers the server and supplies its existing PostgreSQL credentials automatically; no manual connection setup is needed.

This uses Aspire's local single-user configuration, with no separate pgAdmin login or master-password prompt. The web interface grants database administrator access and is intended for local development only. It is independent of Web/Keycloak sign-in and does not block Web readiness. pgAdmin session/preferences are ephemeral; PostgreSQL data remains in the existing persistent volume. Server registration is recreated on each start. The pgAdmin resource is excluded from Aspire deployment manifests.

The image is pinned to `dpage/pgadmin4:9.15.0` (the version selected by Aspire 13.6.5) and its multi-platform SHA-256 digest. No new NuGet dependency is required. Change `Infrastructure:PgAdminPort` if port `5050` is occupied; it must differ from Web and Keycloak ports. For example:

```sh
dotnet run --project src/Marmotte.ChatUi.AppHost --configuration Release --no-build -- --Infrastructure:PgAdminPort=5150
```

## Database lifecycle

The AppHost pins `pgvector/pgvector:0.8.7-pg17-bookworm` and its multi-platform SHA-256 digest, plus `quay.io/keycloak/keycloak:26.6.4` and its digest. See [Program.cs](../src/Marmotte.ChatUi.AppHost/Program.cs) for the exact digests. Upgrades require deliberate image and volume compatibility checks; changing a tag alone does not override the digest.

`marmotte-chatui-postgres-data` mounts `/var/lib/postgresql/data`, the PostgreSQL 17 data path. Both databases persist there. Keycloak uses PostgreSQL rather than an embedded database, so it needs no separate identity volume. The local POC shares the PostgreSQL administrator role across the initializer and consumers; the two databases are separate storage, not separate credential/security boundaries. Production roles and deployment are not implemented.

Aspire creates missing databases on each start. A separate short-lived container then executes:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

This runs on **every AppHost start**, including existing volumes, unlike first-boot image scripts. It only affects `marmotte_chat`; no embeddings or business tables are created. Connection attempts are bounded to 15 seconds, statements to 30 seconds, and lock waits to 10 seconds. `psql` fails on SQL errors, and Web waits for its successful exit. Database schema migrations remain M5 work. The [pgvector documentation](https://github.com/pgvector/pgvector#docker) describes the upstream image and extension.

Changing a PostgreSQL major version requires a migration/export strategy; do not point a new major version at the existing volume. Deleting the volume removes **both application and identity data**. Normal shutdown and the automated checks do not delete the development volume.

## Identity initialization and addresses

[The realm source](../src/Marmotte.ChatUi.AppHost/Realms/marmotte-realm.json) creates realm `marmotte` and confidential client `marmotte-web`. Environment placeholders supply the client secret and browser origin. Authorization Code with PKCE S256 is enabled; implicit, direct-password, service-account, and public registration flows are disabled. No application users or user passwords are seeded.

The default authority is `http://localhost:8180/realms/marmotte`. Its discovery document advertises this browser-visible address. Redirects are restricted to `http://localhost:5180/signin-oidc`; post-logout redirects use `http://localhost:5180/signout-callback-oidc`. The Web OIDC handler processes both callbacks. The realm requires TLS for external access; loopback HTTP and Keycloak `start-dev` are local development only. The AppHost explicitly opts Keycloak out of automatic HTTPS certificate termination so the authority remains identical with and without a development certificate. This uses Aspire 13.6’s experimental certificate API with a narrowly scoped warning suppression; the dashboard retains HTTPS.

Keycloak's startup import skips an existing realm, preserving users and client changes. Editing the JSON, changing Web's port, or changing the configured client secret does **not** update an existing realm. Update its client URLs/secret through the authenticated admin console (and matching AppHost settings), or explicitly create a separate development volume. Do not delete existing data just to apply a configuration change. See [Keycloak import behavior](https://www.keycloak.org/server/importExport#_importing_a_realm_during_startup).

The bootstrap admin is `admin`; its generated password is a secret parameter in the local Aspire dashboard. Create development users there when needed. Bootstrap credentials only create the initial administrator; changing them later is not a password reset.

## Signing in and sessions

In the Keycloak admin console, select realm `marmotte`, open **Users**, and create two enabled development users with distinct usernames, first/last names, and email addresses. Set passwords under **Credentials**; temporary passwords require a change on first login. No account/password is committed. Open Web, choose **Sign in to your workspace**, and enter the user's credentials on Keycloak. Use separate browser profiles or a private window for independent users. The protected workspace displays the signed-in name; chat and persistence are still pending.

Aspire injects the confidential client's settings. Standalone Web accepts the same keys below (environment variables use `__`). Keep the client secret in Web user secrets or a secret store, separately from model-server credentials. All three OIDC keys must be present together. HTTPS is required except for loopback HTTP in Development. Authority URLs cannot contain user information, query strings, or fragments and must match the issuer exactly. The callback origin must match the actual browser URL and Keycloak registration.

| Web key | Default / behavior |
| --- | --- |
| `Authentication:Authority` | No default; Aspire supplies the realm's browser-visible issuer |
| `Authentication:ClientId` | No default; Aspire supplies `marmotte-web` |
| `Authentication:ClientSecret` | Required with OIDC; server secret only |
| `Authentication:SessionMinutes` | `30`; range `1..480`, additionally bounded by ID-token expiry |

The host uses Authorization Code/PKCE, query-mode callbacks, and nonce/correlation checks. OIDC network calls have a 15-second timeout and the sign-in round trip a five-minute deadline. Login always returns to `/workspace`; caller-supplied return destinations and owner IDs are ignored. `/`, `/error`, `/account/error`, static assets, and Development health endpoints are public. Application component routes and SignalR require an active server session. Authentication failures show a generic retry page without provider details.

The HttpOnly, SameSite=Lax cookie contains a protected random ticket reference. Secure cookies are mandatory outside Development; local development uses the request scheme. Only the ID token needed for Keycloak logout remains in the server's in-memory ticket; access/refresh tokens are discarded and identity tokens never enter presentation contracts. Sessions expire absolutely, without sliding renewal or refresh. Expiry or logout denies subsequent application operations immediately, including calls from an already-open circuit; circuit UI state revalidates every 30 seconds. Already-authorized work in progress is not automatically cancelled.

**Sign out** submits an antiforgery-protected POST. Web first revokes the local ticket, then redirects to Keycloak and back to `/`. A remote failure still ends the local session, although Keycloak SSO may remain active. Another user's independent session stays active. Cookie replay after logout is rejected.

This is a single-process POC: Web restart clears all sessions. Multiple instances require a shared ticket/revocation store and protected shared data-protection keys. Keycloak-initiated/back-channel logout, administrator revocation propagation, refresh tokens, and automatic cancellation of in-flight work are not implemented. Remote revocation takes effect at the next sign-in or local expiry. Before production, define those policies, trusted reverse proxies/forwarded headers, HTTPS origins, secret rotation, key protection, and least-privilege database roles. Changing the issuer changes ownership IDs and requires a migration. Database and stored-file authorization still require M5/M9 integration checks.

References: [ASP.NET Core OIDC](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0) and [Blazor authentication state](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/authentication-state?view=aspnetcore-10.0).

## Configuration and secrets

AppHost generates and persists these secret parameters in its development user-secrets store on first use:

| AppHost key | Consumers |
| --- | --- |
| `Parameters:postgres-password` | PostgreSQL, initializer, Keycloak database connection, Web database connection |
| `Parameters:keycloak-password` | Keycloak bootstrap administrator only |
| `Parameters:keycloak-client-secret` | Keycloak realm import and Web authentication configuration |

Keep these values stable while retaining the volume. PostgreSQL's startup password setting does not change an existing database role's password. A changed/missing secret alongside an existing volume can cause authentication failures; restore the matching secret or perform an explicit password change before updating consumers.

Both hosts have stable `UserSecretsId` values; stores are separate and unencrypted local development storage. Use `dotnet user-secrets set` with the AppHost project to supply an actual local value. A harmless syntax check uses an unused setting:

```sh
dotnet user-secrets set "Development:ExampleSecret" "local-example-only" --project src/Marmotte.ChatUi.AppHost
dotnet user-secrets remove "Development:ExampleSecret" --project src/Marmotte.ChatUi.AppHost
```

No credentials belong in tracked JSON, launch profiles, logs, or examples. Model credentials will be separate from OIDC credentials. Ordinary AppHost overrides are:

| Key | Default | Meaning |
| --- | --- | --- |
| `Infrastructure:WebPort` | `5180` | Published Web port and initial realm's browser origin |
| `Infrastructure:KeycloakPort` | `8180` | Published Keycloak port and authority |
| `Infrastructure:PgAdminPort` | `5050` | Published local pgAdmin web port |
| `Infrastructure:PostgresVolume` | `marmotte-chatui-postgres-data` | Existing or new Docker volume; overrides isolate test/checkouts |

Ports must be distinct and within `1024..65535`. For example, pass `-- --Infrastructure:WebPort=5280 --Infrastructure:KeycloakPort=8280` to the AppHost `dotnet run` command. Apply the existing-realm URL rule above. Standalone Web uses its own launch profile instead.

AppHost injects `ConnectionStrings__chatdb`, Keycloak service-discovery endpoints, `Authentication__Authority`, `Authentication__ClientId`, and `Authentication__ClientSecret` into the **server** Web process. It does not forward the Keycloak administrator password or identity database connection. Keycloak's JDBC address uses Aspire's container-facing PostgreSQL endpoint; Web's connection uses the host-facing endpoint. No connection configuration enters Domain, Contracts, or Components.

## Health and diagnostics

ServiceDefaults registers service discovery for `HttpClient`, a 30-second default HTTP timeout, runtime/server/client HTTP metrics and traces, and optional OTLP export when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured by Aspire. It adds no automatic retries; model generation will define its own timeouts/cancellation in its adapter. Body/custom-header capture, SQL instrumentation, exception recording in HTTP traces, and OpenTelemetry log export are not enabled. HTTP instrumentation keeps its default query-value redaction; do not disable it or include secrets/content in URL paths. Application logging must continue to avoid conversations, file contents, base64 data, and credentials.

In Development, `/alive` runs only checks tagged `live`; `/health` runs all registered checks and returns 503 on failure. Responses contain only status, without diagnostic details. Polling these endpoints is excluded from HTTP traces. Web currently checks only itself: Aspire startup gates verify dependencies, while ongoing repository/OIDC dependency health checks remain unimplemented. Neither endpoint is mapped in Production. `Logging:LogLevel` controls console diagnostics; no request-body logging is configured. Treat dashboard access as privileged because it can expose resource configuration and secrets.

## Model-server network topology

Web runs as a host process, so a future model URL using `localhost` refers to this development computer. Use the actual model server's configured address and port; M3 makes no model request or compatibility claim. Inside a container, `localhost` refers to that container. A future containerized adapter must use an Aspire-provided endpoint or the container engine's host gateway (for example `host.docker.internal` where supported), with a server listening on a reachable interface and suitable firewall/authentication settings. A model server on another computer needs that computer's reachable address. These settings never follow the browser's location automatically. On 2026-11-26, a temporary HTTP probe on this macOS ARM64/Rancher Desktop host was reached both from a host process through loopback and from the pinned PostgreSQL image through `host.docker.internal`. This verifies that local network route, not LM Studio/Unsloth API behavior or other container engines.

## Verification and troubleshooting

The opt-in [infrastructure test](../tests/Marmotte.ChatUi.Infrastructure.Tests/InfrastructureTests.cs) starts the actual AppHost with random secrets, distinct ports, and a unique temporary volume. It checks readiness, the pgAdmin web interface, separate databases, vector availability, browser-visible discovery, the confidential client/PKCE/redirects, and retained application/identity data. It also signs two temporary users in through real Keycloak forms with independent cookies, follows callbacks, checks antiforgery, signs out through Keycloak, rejects a revoked cookie, and preserves the other session. Test public ports are selected once and retained so advertised OIDC origins remain reachable. Restarts cover both an already-enabled extension and recovery after its removal. It disposes resources and removes only its own test volume, including after failures. Routine tests skip it; CI has a separate Docker job. See [development](development.md) for the command.

- SDK/restore failure: check `dotnet --info`, NuGet access, and the pinned lock files. Do not disable auditing or locked mode to hide failures.
- Docker unavailable: start the engine and verify `docker version` shows both client and server. Image-pull failures require access to Docker Hub/Quay. Wait for first downloads and Keycloak initialization.
- Resource remains waiting: inspect the dependency immediately before it in the table above. `database-init` must exit successfully; check PostgreSQL credentials, extension availability, and permissions if it fails.
- Port/certificate failure: stop the conflicting process, set the supported ports, or repair the development certificate. Dashboard/OTLP/resource-service ports are in AppHost's launch profile.
- Keycloak redirect or issuer mismatch: use `localhost` consistently and inspect discovery/client URLs. Existing realms are not reimported; update the client explicitly after a port change.
- Data seems missing: verify the Docker engine/context and selected volume. Do not delete a volume or regenerate secrets before locating the original data.
- Test interruption: a force-killed test process may leave a `marmotte-infrastructure-test-*` volume. Inspect its exact name and ensure no container uses it before removing that test volume; never prune all volumes.
- Sign-in unavailable: verify all three `Authentication` settings, discovery reachability from Web, matching client secrets on existing realms, and the registered redirect origin. The error page omits provider diagnostics.
- Repeated sign-in: ID-token expiry may be earlier than the session ceiling; sign in again. A Web restart also ends all sessions.
- Model/repository settings appear inactive in Web: these adapters remain future milestones.
