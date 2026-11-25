# Marmotte.ChatUi

English | [Français](README.fr.md)

![Marmotte.ChatUi: the turquoise mascot with a red scarf behind a wooden sign](docs/images/marmotte-chatui-banner.png)

A .NET and Blazor chat application, with reusable components for embedding conversations and chat widgets in other applications.

## Project status

> **Experimental proof of concept (POC) — not production-ready.**
> This project is intended for experimentation and evaluation, not production use.

<img src="docs/images/references/experimental-proof-of-concept.png" alt="The marmot mascot holding an Experimental Proof of Concept sign" width="320" />

The project is in its planning phase. This repository contains the architecture, contribution guidelines, and visual assets. The application, integrations, and NuGet package have not yet been implemented or published.

## Product direction

- Blazor components intended for distribution through NuGet.
- Chat and connection settings for OpenAI-compatible APIs, including LM Studio and Unsloth Desktop.
- The OpenAI .NET SDK considered for an independent adapter.
- Conversation history and user preferences in PostgreSQL, with pgvector available.
- A complete application with OIDC/Keycloak authentication and .NET Aspire orchestration.
- A customizable design system, light and dark modes, and image uploads through file selection and drag and drop.
- PDF preparation for vision models planned for a later stage.

## Documentation

- [Architecture and scope](ARCHITECTURE.md)
- [Contribution guidelines](AGENTS.md)
- [Mascot and image provenance](docs/images/README.md)

Startup instructions will be added when an executable application is available.
