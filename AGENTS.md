# Contribution guidelines — Marmotte.ChatUi

English | [Français](AGENTS.fr.md)

## Scope and reference documents

These guidelines apply throughout the repository unless an AGENTS.md file closer to the code specifies otherwise.

Read [ARCHITECTURE.md](ARCHITECTURE.md) before making structural changes. That document describes technical decisions, boundaries, and product milestones; this document describes how to design, write, document, and verify the code.

The project is at an early stage. Do not present planned components, integrations, or commands as already available. Keep the documentation aligned with the actual implementation.

## SOLID and design

- Give each class, service, and component an explicit responsibility. Separate rendering, business orchestration, transport, and persistence.
- Isolate variations behind focused contracts: model provider, storage, identity, or attachment preparation.
- Preserve contract behavior in every implementation, including errors, cancellation, and data access guarantees.
- Prefer several small, cohesive interfaces to a universal interface.
- Make use cases depend on internal abstractions. Adapters implement those contracts; the host application composes the implementations.
- Introduce abstractions for concrete responsibilities. Avoid mechanical layers, generic services, and interfaces added without a reason.

## DDD-inspired modeling

Use consistent terminology for conversations, messages, attachments, connection profiles, and user preferences. Explicitly distinguish the domain model from the AI model.

Express invariants in the objects responsible for them. Use aggregates, value objects, and domain services when they clarify those rules. Keep business rules out of Razor components, database mappings, and provider clients.

The domain remains independent of Blazor, the OpenAI SDK, PostgreSQL, the ORM, Keycloak, and Aspire. Use cases orchestrate the domain and ports; they do not know about technical implementations.

Do not mandate CQRS, a mediator, domain events, or microservices as a matter of principle. Separate assemblies support the boundaries described in the architecture and do not imply separately deployed services.

## Boundaries and dependencies

- Place repository contracts and other outbound ports in the application core, independently of providers.
- Keep the PostgreSQL implementation and its migrations in a separate persistence assembly.
- Do not expose DbContext, IQueryable, ORM entities, Npgsql connections, or pgvector types outside persistence adapters.
- If adopted after validation, the OpenAI package stays in its dedicated adapter. No SDK type crosses its public contracts.
- Blazor components distributed through NuGet must not require PostgreSQL, Keycloak, Aspire, or the OpenAI SDK in the consuming application.
- Components receive explicit parameters, events, and presentation contracts. They do not directly read secrets or the full application's global configuration.
- Register and compose services at the host application's composition boundary.

## Code style

Write readable, well-spaced code that is easy to scan. Use precise names, functions focused on a single intent, and early returns when they reduce nesting. Separate logical steps with blank lines; avoid compact expressions that obscure behavior.

Keep terminology and C# conventions consistent throughout the solution. Prefer English technical names and follow the bilingual Markdown convention below for documentation. Use nullable annotations and handle missing or invalid values explicitly.

For I/O, use asynchronous APIs and propagate CancellationToken through to the adapters. Avoid .Result, .Wait(), and long-running operations that block Blazor rendering. Define timeouts and cancellation behavior.

Do not mix unrelated refactoring into a feature change. Limit dependencies to actual needs and explain their role.

## Detailed, useful documentation

### Markdown languages and naming

Every Markdown document must have an English primary version named `[name].md` and a French translation named `[name].fr.md` in the same directory. This includes README, ARCHITECTURE, AGENTS, and documentation in subdirectories.

Update both versions in the same change and keep their meaning, scope, and technical decisions aligned. Translate prose, headings, image descriptions, and diagram labels; preserve identifiers, commands, file paths, URLs, and exact product names.

Add a link to the other language near the top of each document. Local links to other Markdown documents should target the matching language; both versions share the same code and image assets.

### Content and maintenance

Document decisions, their reasons, invariants, and limitations. Comments explain an intention or constraint; they do not restate every line of code.

Document every explicitly declared C# type with an XML documentation summary explaining its responsibility, regardless of accessibility. This includes `private` and `internal` types, nested types, and test helpers. For non-public helpers, also document relevant preconditions, results, errors, and side effects so their contracts remain understandable to maintainers.

Document reusable public APIs: purpose, parameters, return values, errors, cancellation, ownership, and lifetimes where relevant. For Blazor components, describe parameters, events, templates, states, accessibility, and integration examples.

Maintain the following documents alongside the code:

- README: actual project status, prerequisites, startup instructions, and documentation entry points.
- ARCHITECTURE: responsibilities, dependencies, flows, decisions, and limitations.
- Integration documentation: NuGet installation, theme customization, and connection to the host's services.
- Operations documentation: configuration, migrations, identity, storage, and troubleshooting.

Document configuration formats and values without publishing secrets. Verify example commands before presenting them as executable.

## UI and design system

Use the design system's semantic tokens for colors, spacing, typography, radii, shadows, and interactive states. Avoid scattered component colors and global selectors that modify the consuming application.

Preserve light, dark, and system modes, along with host-provided token overrides, ideally at the widget-instance level. Check contrast, keyboard interactions, labels, focus, and accessible announcements of relevant states.

Explicitly represent sending, streaming, cancellation, attachment preparation, and error states. Do not show an attachment as sent if the model or adapter does not support it.

Reuse the supplied mascot for the product identity. Keep the original sources and their provenance; preserve the distinctive colors and details when creating variants.

## Connections, data, and files

Validate addresses, model parameters, and capabilities that are actually supported. Do not equate “OpenAI-compatible” with complete support for every OpenAI API. Do not hardcode a port, version, or capability without verification.

Distinguish user OIDC authentication from credentials used to access model servers. The UI requires a key only when the server does; handle and document any SDK constraints inside the adapter.

Verify on the server that each conversation, preference, and attachment belongs to the authorized user. Do not trust an owner identifier received from the browser.

Do not commit or log secrets. Do not log conversation text, attachment contents, or their base64 representations by default. Ordinary settings and secrets require separate handling.

Treat uploaded files as untrusted data. Check size, actual format, dimensions, and access rights. Bound PDF rendering by pages, pixels, memory, duration, and concurrency. Propagate cancellation and clean up temporary files.

## Verification and delivery

Match verification to the change: core invariants and authorization, adapter contract tests, PostgreSQL/pgvector integration, Blazor checks, and NuGet consumption tests as these parts are implemented.

Test observable behavior and important edge cases. Avoid tests that merely mirror the implementation. Routine tests must not depend on a personal server or a paid API key.

For documentation-only changes, review the content, verify local links and language pairs, and inspect the diff. For code changes, run the relevant available checks and report any that could not be performed.

Do not claim a build, compatibility check, NuGet publication, or deployment that has not happened. Describe what changed, why, what was verified, and any remaining limitations.

## Disclose AI assistance in commits

- Every commit created or materially assisted by an AI agent must identify that agent in the commit message for transparency. This applies to ChatGPT Codex, Mistral, Claude, and other AI assistants.
- Preserve the human author's configured Git identity. Add attribution trailers after a blank line at the end of the commit message; do not impersonate the human or replace their Git configuration.
- For ChatGPT Codex, use `Co-authored-by: ChatGPT Codex <noreply@openai.com>`.
- For Mistral, Claude, or another assistant, use a `Co-authored-by: Name <email>` trailer when an attribution identity has been supplied or configured. Otherwise, use an explicit `AI-Assisted-By: Mistral` or `AI-Assisted-By: Claude` trailer instead of inventing an email address.
