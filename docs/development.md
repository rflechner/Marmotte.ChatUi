# Development

English | [Français](development.fr.md)

## Prerequisites and versions

The solution uses .NET 10 LTS, C# 14, and `net10.0` throughout. [global.json](../global.json) selects SDK `10.0.401`, allows servicing patches within `10.0.4xx`, and excludes previews. Installing only .NET 11 does not satisfy this selection. See the [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

| Dependency | Selected version | Purpose |
| --- | --- | --- |
| Aspire.AppHost.Sdk / Aspire.Hosting.AppHost | 13.6.5 | Local infrastructure orchestration |
| Aspire.Hosting.PostgreSQL / Aspire.Hosting.Testing | 13.6.5 | PostgreSQL resources and real AppHost integration tests |
| Aspire.Hosting.Keycloak | 13.6.5-preview.1.26559.7 | Official preview hosting integration matching Aspire 13.6.5 |
| Microsoft.Extensions.ServiceDiscovery | 10.11.1 | Host HTTP service resolution |
| OpenTelemetry exporter/hosting and ASP.NET Core/HTTP/runtime instrumentation | 1.21.0 | Optional metrics and traces |
| Microsoft.AspNetCore.Components.Web | 10.0.12 | Browser-compatible Razor Class Library references |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | 10.0.12 | Host-only OIDC protocol handler |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | In-process web host tests |
| Microsoft.EntityFrameworkCore / Relational / Design / dotnet-ef | 10.0.12 | Code-first model, migrations, and local design-time tooling |
| Npgsql / Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | PostgreSQL driver and EF provider inside the persistence adapter |
| Microsoft.AspNetCore.DataProtection.Abstractions | 10.0.12 | Host-provided credential protection |
| xunit.v3 | 4.0.3 | Tests using Microsoft.Testing.Platform |

Direct package versions live in [Directory.Packages.props](../Directory.Packages.props). The Aspire SDK version lives in `global.json`; update the SDK and hosting package together. Transitive versions and content hashes are recorded in committed lock files. [NuGet.Config](../NuGet.Config) selects only nuget.org, avoiding accidental dependencies on a developer's private feeds.

The AppHost uses the SDK's NuGet-distributed dashboard and orchestrator, so building does not require a separately installed Aspire CLI or workload. `AspireUseCliBundle=false` is intentional; only diagnostic `ASPIRE010` is suppressed for this choice. CLI-only features are outside M1. Aspire selects native tool packages by build host, so AppHost and Infrastructure.Tests use `packages.<RID>.lock.json`. Lock files exist for `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-x64`, `osx-arm64`, `win-x64`, and `win-arm64`. Their restoration does not establish runtime validation on every platform.

## Repeatable checks

From the repository root:

```sh
dotnet tool restore
dotnet restore Marmotte.ChatUi.slnx --locked-mode
dotnet build Marmotte.ChatUi.slnx --configuration Release --no-restore
dotnet format Marmotte.ChatUi.slnx --verify-no-changes --no-restore
ConnectionStrings__chatdb='Host=localhost;Database=marmotte_design;Username=design' dotnet ef migrations has-pending-model-changes --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release --no-build
dotnet test --solution Marmotte.ChatUi.slnx --configuration Release --no-build --no-restore
```

The `--solution` option is required by the selected Microsoft.Testing.Platform mode. See [xUnit's MTP integration](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform). Docker, PostgreSQL, Keycloak, a personal model server, and paid credentials are unnecessary for these checks. The first restore needs network access to NuGet.

[CI](../.github/workflows/ci.yml) runs the same commands on Ubuntu and has a separate Docker infrastructure job. Configuring the workflow does not establish that GitHub Actions has already run it.

The architecture and host tests enforce allowed project references, core independence, component dependency isolation, public static rendering, authenticated Interactive Server markers, static assets, and HTTP 404 behavior. Host tests use ephemeral data protection. Health/readiness/liveness, Production health endpoint exclusion, and the absence of an OTLP log provider are covered. M4 uses the actual OIDC/cookie handlers with RSA-signed local test tokens to check PKCE, issuer/audience/nonce/lifetime validation, missing identity, anonymous entry points, antiforgery, cookie replay, failed remote logout, expiry, and cross-user use cases with the actual current-user adapter. Test repository code is shared with Core.Tests through a linked source file; these routine tests keep their test repositories; opt-in M5 tests use the host-composed PostgreSQL repositories. The EF model/snapshot check also runs without a database; see the [migration workflow](persistence.md#ef-migration-workflow). Routine tests need no personal server, paid key, or Docker; opt-in database tests require Docker. Browser JavaScript/WebSocket behavior, model compatibility, and external NuGet consumption remain future checks.

M2 adds `Marmotte.ChatUi.Core.Tests`: immutable domain snapshots, history restoration, lifecycle/partial responses, duplicate submissions, optimistic concurrency, user isolation, bounded paging, settings validation, and cancellation. Its test-only repositories require no infrastructure. See the [core reference](core.md) for adapter guarantees and limitations.

## Docker infrastructure checks

With Docker running, after restore/build:

```sh
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Infrastructure.Tests --configuration Release --no-build --no-restore
```

This POSIX-shell command enables the otherwise skipped container test; in PowerShell set `$env:MARMOTTE_RUN_INFRASTRUCTURE_TESTS = "1"` before the same `dotnet test` invocation. The actual AppHost runs with its dashboard enabled, fresh random secrets/ports, and a unique volume that is removed afterward. Three starts verify initial activation, idempotent activation, recovery after extension removal, retained SQL data and a Keycloak user, readiness, browser-visible OIDC discovery, and imported client restrictions. M4 additionally signs in two users through real Keycloak login forms, checks antiforgery and RP-initiated logout, rejects revoked cookies, and preserves independent sessions. The test fixes the chosen public origins after dashboard testing defaults are applied. Its HTTP cookie jars use a Safari user-agent to select [Keycloak’s loopback HTTP cookie compatibility](https://github.com/keycloak/keycloak/blob/26.6.4/services/src/main/java/org/keycloak/utils/SecureContextResolver.java); this does not simulate a browser engine or change realm security. It has a five-minute deadline and needs no personal server or paid key. M5 pins EF Core/Npgsql in the persistence adapter; this test also verifies the business migration history on each start. The separate [PostgreSQL suite](persistence.md#verification) checks repository behavior and authenticated host/database restarts.

Pinned container tags/digests, local secrets, and lifecycle behavior are documented in [operations](operations.md). Tests do not use or delete the normal development volume. A forced process termination can bypass cleanup; see the troubleshooting instructions before removing a leftover test volume. CI configuration is not evidence of a completed GitHub Actions run.

## Updating dependencies

Change central versions deliberately, run `dotnet restore Marmotte.ChatUi.slnx` to update lock files, review the resulting dependency changes, then run the locked checks again. For an Aspire upgrade, regenerate all AppHost and Infrastructure.Tests lock files using each RID listed above; for example:

```sh
dotnet restore tests/Marmotte.ChatUi.Infrastructure.Tests/Marmotte.ChatUi.Infrastructure.Tests.csproj -p:NETCoreSdkRuntimeIdentifier=linux-x64
dotnet restore Marmotte.ChatUi.slnx --locked-mode
```

The first command resolves packages for that build host; it does not cross-compile or validate execution there. The second command restores your own host's assets before building. Never resolve a locked-restore failure by silently disabling locked mode in CI.

## C# and Razor conventions

- Follow [AGENTS.md](../AGENTS.md) and the dependency boundaries in [ARCHITECTURE.md](../ARCHITECTURE.md).
- `.editorconfig` defines UTF-8, LF, final newlines, four-space C#/Razor indentation, and two-space project/configuration indentation. Use file-scoped C# namespaces, explicit accessibility, braces, and readable spacing. Razor indentation is also reviewed manually; `dotnet format` does not validate every Razor or CSS rule.
- Nullable references, the .NET 10 recommended analyzers, style enforcement, and warnings as errors apply solution-wide. Avoid broad warning suppressions. Test method names may use underscores.
- Keep components focused on presentation. Do not put provider calls, persistence, secrets, or implicit server-circuit dependencies into the RCL. The host chooses the render mode.
- Add focused behavior tests with each use case. Test adapters stay in test projects; PostgreSQL is implemented, while the model adapter remains reserved for M6.
- Every Markdown change includes the matching English/French version and matching-language local links.

See [operations](operations.md) for local startup and secrets, and [integration](integration.md) for the rendering contract.
