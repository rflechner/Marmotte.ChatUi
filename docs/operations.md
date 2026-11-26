# Local operations

English | [Français](operations.fr.md)

## Current scope

M1 provides a local web host and an Aspire AppHost containing only the `web` resource. No database, identity provider, model connection, migration, attachment store, or health/telemetry setup is implemented yet. ServiceDefaults is an empty assembly reserved for M3. No production deployment is configured. M2 adds core use cases and test adapters only; Web does not register them, and there are no new runtime configuration keys. Connection-profile validation and the separation of settings from future credential storage are described in the [core reference](core.md).

## Start and stop

First run the restore and Release build commands in the [development guide](development.md). To start the web host directly:

```sh
dotnet run --project src/Marmotte.ChatUi.Web --configuration Release --no-build
```

The development profile binds [http://localhost:5180](http://localhost:5180). It displays a foundation page and the existing mascot. Ctrl+C stops the host. There is no application data to retain at this stage.

To start the same web host through Aspire:

```sh
dotnet dev-certs https --check
dotnet run --project src/Marmotte.ChatUi.AppHost --configuration Release --no-build
```

Aspire needs a valid development HTTPS certificate. If missing or untrusted, follow the [.NET development certificate instructions](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs) for your platform. The AppHost's HTTPS profile uses dashboard port `17291`, OTLP port `21291`, and resource-service port `22291`. Use the authenticated dashboard link printed in the terminal; do not share its login token. The web endpoint remains port `5180`. These are repository-owned development defaults, not assumed ports of model servers.

Ctrl+C in the AppHost terminal stops orchestration and the child web process. Do not run the direct web host and AppHost simultaneously on the same web port. Docker is not required until container resources are introduced in M3.

## Configuration and development secrets

The web host reads ordinary settings from `appsettings.json`, standard ASP.NET Core environment overrides, and command-line configuration. `Logging:LogLevel` controls diagnostics and `AllowedHosts` restricts accepted host headers to local hostnames. Launch profiles set `Development` and local addresses. Environment variables use `__` in place of `:` for nested keys, such as `Logging__LogLevel__Default`.

Both hosts have stable `UserSecretsId` values in their project files, so initialization is unnecessary. ASP.NET Core/Aspire load their respective secret stores in development. A harmless, unused example shows the syntax:

```sh
dotnet user-secrets set "Development:ExampleSecret" "local-example-only" --project src/Marmotte.ChatUi.Web
dotnet user-secrets remove "Development:ExampleSecret" --project src/Marmotte.ChatUi.Web
```

Use the AppHost project path instead for future orchestration secrets. The two stores are separate; a secret in AppHost is not automatically forwarded to Web. No secret or API key is required in M1, and `Development:ExampleSecret` is not an application setting.

User secrets are local development storage, not encrypted production storage. Do not put credentials in `appsettings.json`, launch profiles, tracked files, logs, or example values. Future OIDC credentials and model-server credentials are distinct and will be documented with their adapters.

## Troubleshooting

- SDK not found: install SDK `10.0.401` or a newer `10.0.4xx` patch; check `dotnet --info` from the repository root.
- Restore fails: verify NuGet network access and the selected SDK. Do not use personal feeds or disable dependency auditing to hide failures. Intentional dependency changes require reviewed lock-file updates.
- Port already in use: stop the earlier process or adjust the relevant `Properties/launchSettings.json` development addresses together.
- Aspire certificate error: check the certificate and platform trust configuration; the standalone HTTP host remains available for M1 development.
- Tests report a VSTest/MTP mismatch: run from the repository so `global.json` is found, and use `dotnet test --solution ...` as documented.
- A model, database, identity, or migration setting appears to do nothing: these features are not implemented in M1. Follow the [roadmap](../roadmap.md) rather than supplying speculative configuration keys.
