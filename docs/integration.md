# Component integration

English | [Français](integration.fr.md)

## What exists in M1

`Marmotte.ChatUi.Components` is a `net10.0` Razor Class Library referencing `Marmotte.ChatUi.Contracts` and `Microsoft.AspNetCore.Components.Web`. It has no public chat components or service contracts yet. Its package graph is checked for independence from PostgreSQL, Keycloak, Aspire, and OpenAI adapters. The web project already references the RCL, but currently renders its own foundation page.

There is no published NuGet package or supported installation command. All projects are currently non-packable; package identity, metadata, delivery of Contracts, and external consumption checks belong to M10. M7 will add actual component examples, parameters, callbacks, templates, states, accessibility guidance, and theme customization APIs.

## Rendering contract

The complete application uses Interactive Server with prerendering. It registers `AddInteractiveServerComponents`, maps `AddInteractiveServerRenderMode`, and sets `InteractiveServer` on Routes and HeadOutlet. The initial host test verifies prerendered content and server interactivity markers; it does not simulate a browser circuit.

Reusable components must inherit their host's mode, avoiding a hardcoded `@rendermode`, server-only injected services, or implicit access to `HttpContext`, secrets, and global application configuration. See [Blazor render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0).

| Mode | Contract and validation scope |
| --- | --- |
| Static SSR / prerendering | Display-only output must be safe before interactivity; sending and other callbacks require an interactive host. |
| Interactive Server | First interactive integration target, configured in the M1 web host. Actual chat components will be tested in M7. |
| Interactive WebAssembly / Auto | Preserve compatibility in the library design. No client host or consumption test exists yet; support is not verified. Future services must be supplied by the consuming host. |

The CSS under Web is only a minimal host shell with light/system-dark styling. It is not the public token API or complete theme selector planned for M7. The mascot belongs to the host's identity; consumers of future widgets must be able to replace or omit it.

See [architecture](../ARCHITECTURE.md) for dependency direction and [development](development.md) for the checks protecting these boundaries.
