# Marmotte.ChatUi

English | [Français](README.fr.md)

![Marmotte.ChatUi: the turquoise mascot with a red scarf behind a wooden sign](docs/images/marmotte-chatui-banner.png)

A .NET and Blazor chat application, with reusable components for embedding conversations and chat widgets in other applications.

## Project status

> **Experimental proof of concept (POC) — not production-ready.**
> This project is intended for experimentation and evaluation, not production use.

<img src="docs/images/references/experimental-proof-of-concept.png" alt="The marmot mascot holding an Experimental Proof of Concept sign" width="320" />

Milestones 1 and 2 are implemented: the .NET 10/Blazor/Aspire foundations, the domain model, authorized conversation and settings use cases, provider-neutral ports, presentation contracts, and automated tests. The core runs with test adapters; Web still displays the foundation page. Real persistence, identity/model adapters, reusable chat components, and ServiceDefaults behavior remain for later milestones. No NuGet package has been published.

## Product direction

- Blazor components intended for distribution through NuGet.
- Chat and connection settings for OpenAI-compatible APIs, including LM Studio and Unsloth Desktop.
- The OpenAI .NET SDK considered for an independent adapter.
- Conversation history and user preferences in PostgreSQL, with pgvector available.
- A complete application with OIDC/Keycloak authentication and .NET Aspire orchestration.
- A customizable design system, light and dark modes, and image uploads through file selection and drag and drop.
- PDF preparation for vision models planned for a later stage.

## Documentation

- [Roadmap and numbered milestones](roadmap.md)
- [Architecture and scope](ARCHITECTURE.md)
- [Domain model, ports, and use cases](docs/core.md)
- [Contribution guidelines](AGENTS.md)
- [Mascot and image provenance](docs/images/README.md)

## Build and run

Install the .NET SDK selected by [global.json](global.json): `10.0.401` or a newer patch in the same `10.0.4xx` feature band. NuGet access is required for the first restore. Docker, a model server, and credentials are not required for M1/M2.

Run from the repository root:

```sh
dotnet restore Marmotte.ChatUi.slnx --locked-mode
dotnet build Marmotte.ChatUi.slnx --configuration Release --no-restore
dotnet format Marmotte.ChatUi.slnx --verify-no-changes --no-restore
dotnet test --solution Marmotte.ChatUi.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Marmotte.ChatUi.Web --configuration Release --no-build
```

Open [the local host](http://localhost:5180). The page displays the project status and mascot; it does not yet provide chat. Stop it with Ctrl+C.

See the [development guide](docs/development.md) for pinned dependencies, conventions, and test coverage; [operations](docs/operations.md) for Aspire startup and development secrets; and [integration](docs/integration.md) for component boundaries and rendering decisions.
