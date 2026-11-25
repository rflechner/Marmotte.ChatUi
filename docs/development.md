# Development

English | [Français](development.fr.md)

## Prerequisites and versions

M1 uses .NET 10 LTS, C# 14, and `net10.0` throughout. [global.json](../global.json) selects SDK `10.0.401`, allows servicing patches within `10.0.4xx`, and excludes previews. Installing only .NET 11 does not satisfy this selection. See the [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

| Dependency | Selected version | Purpose |
| --- | --- | --- |
| Aspire.AppHost.Sdk / Aspire.Hosting.AppHost | 13.6.5 | Web-only local orchestration |
| Microsoft.AspNetCore.Components.Web | 10.0.12 | Browser-compatible Razor Class Library references |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | In-process web host tests |
| xunit.v3 | 4.0.3 | Tests using Microsoft.Testing.Platform |

Direct package versions live in [Directory.Packages.props](../Directory.Packages.props). The Aspire SDK version lives in `global.json`; update the SDK and hosting package together. Transitive versions and content hashes are recorded in committed lock files. [NuGet.Config](../NuGet.Config) selects only nuget.org, avoiding accidental dependencies on a developer's private feeds.

The AppHost uses the SDK's NuGet-distributed dashboard and orchestrator, so building does not require a separately installed Aspire CLI or workload. `AspireUseCliBundle=false` is intentional; only diagnostic `ASPIRE010` is suppressed for this choice. CLI-only features are outside M1. Aspire selects native tool packages by build host, so AppHost uses `packages.<RID>.lock.json`. Lock files exist for `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-x64`, `osx-arm64`, `win-x64`, and `win-arm64`. Their restoration does not establish runtime validation on every platform.

## Repeatable checks

From the repository root:

```sh
dotnet restore Marmotte.ChatUi.slnx --locked-mode
dotnet build Marmotte.ChatUi.slnx --configuration Release --no-restore
dotnet format Marmotte.ChatUi.slnx --verify-no-changes --no-restore
dotnet test --solution Marmotte.ChatUi.slnx --configuration Release --no-build --no-restore
```

The `--solution` option is required by the selected Microsoft.Testing.Platform mode. See [xUnit's MTP integration](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform). Docker, PostgreSQL, Keycloak, a personal model server, and paid credentials are unnecessary for these checks. The first restore needs network access to NuGet.

[CI](../.github/workflows/ci.yml) runs the same commands on Ubuntu. Configuring the workflow does not establish that GitHub Actions has already run it.

The initial tests enforce allowed project references, core independence from external packages, component dependency isolation, home-page prerendering and Interactive Server markers, static assets, and HTTP 404 behavior. Host tests use an ephemeral data-protection provider and do not require development secrets. Browser interaction, database access, OIDC, model compatibility, and external NuGet consumption belong to later milestones.

## Updating dependencies

Change central versions deliberately, run `dotnet restore Marmotte.ChatUi.slnx` to update lock files, review the resulting dependency changes, then run the locked checks again. For an Aspire upgrade, regenerate all AppHost lock files using each RID listed above; for example:

```sh
dotnet restore src/Marmotte.ChatUi.AppHost/Marmotte.ChatUi.AppHost.csproj -p:NETCoreSdkRuntimeIdentifier=linux-x64
dotnet restore Marmotte.ChatUi.slnx --locked-mode
```

The first command resolves packages for that build host; it does not cross-compile or validate execution there. The second command restores your own host's assets before building. Never resolve a locked-restore failure by silently disabling locked mode in CI.

## C# and Razor conventions

- Follow [AGENTS.md](../AGENTS.md) and the dependency boundaries in [ARCHITECTURE.md](../ARCHITECTURE.md).
- `.editorconfig` defines UTF-8, LF, final newlines, four-space C#/Razor indentation, and two-space project/configuration indentation. Use file-scoped C# namespaces, explicit accessibility, braces, and readable spacing. Razor indentation is also reviewed manually; `dotnet format` does not validate every Razor or CSS rule.
- Nullable references, the .NET 10 recommended analyzers, style enforcement, and warnings as errors apply solution-wide. Avoid broad warning suppressions. Test method names may use underscores.
- Keep components focused on presentation. Do not put provider calls, persistence, secrets, or implicit server-circuit dependencies into the RCL. The host chooses the render mode.
- Add focused behavior tests with each future use case; empty assemblies intentionally contain no placeholder classes or speculative interfaces.
- Every Markdown change includes the matching English/French version and matching-language local links.

See [operations](operations.md) for local startup and secrets, and [integration](integration.md) for the rendering contract.
