# Component integration

English | [Français](integration.fr.md)

## Available surface

M7 implements a `net10.0` Razor Class Library with only `Marmotte.ChatUi.Contracts` and the browser-compatible `Microsoft.AspNetCore.Components.Web` package. Components accept snapshots, parameters, callbacks and templates; they inject no application services and access no configuration, identity, storage, or model clients. The full Web host uses the same widget in its protected workspace, with sending disabled until M8 connects the chat workflow.

The independent [sample](../samples/Marmotte.ChatUi.Sample/Marmotte.ChatUi.Sample.csproj) references only Components. It substitutes a deterministic response service, demonstrates both inherited and instance themes, and uses the original mascot through an unchanged deployment copy. It needs no Keycloak, PostgreSQL, Aspire, OpenAI SDK, credentials, or network calls to a model. State lasts for the page's circuit; reloading loses it. Start after the solution restore/build:

```sh
dotnet run --project samples/Marmotte.ChatUi.Sample --configuration Release --no-build
```

Open [the component studio](http://127.0.0.1:5190). The launch profile selects Development so referenced static web assets resolve from the checkout. Stop with Ctrl+C. This is a local demonstration, not an application deployment.

No NuGet package has been published. Projects remain non-packable; package identity, metadata, Contracts packaging and external package consumption belong to M10. Use a project reference for now.

## Add the library to a host

Reference `src/Marmotte.ChatUi.Components/Marmotte.ChatUi.Components.csproj` from your application and import `Marmotte.ChatUi.Components` and `Marmotte.ChatUi.Contracts` in `_Imports.razor`. Load this stylesheet once in the host document:

```html
<link rel="stylesheet" href="_content/Marmotte.ChatUi.Components/marmotte-chatui.css" />
```

For Blazor Web Apps, the sample uses `@Assets["_content/Marmotte.ChatUi.Components/marmotte-chatui.css"]` with `MapStaticAssets()` for fingerprinted assets. This follows the [Blazor RCL asset convention](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/class-libraries?view=aspnetcore-10.0). The library needs no service registration and imposes no `@rendermode`. The host must establish interactivity to use events; the sample configures Interactive Server on Routes and HeadOutlet.

A small host-controlled integration is:

```razor
<ChatWidget Title="Notes" Messages="_messages"
            @bind-Draft="_draft" @bind-Theme="_theme"
            Status="_status" OnSend="AcceptAsync" OnCancel="StopAsync">
    <Branding><span aria-hidden="true">✦</span></Branding>
    <EmptyContent><h3>Start with a question.</h3></EmptyContent>
</ChatWidget>
```

`_messages` is `IReadOnlyList<MessageView>`, `_draft` is `string`, `_theme` is `ThemePreference`, and `_status` is nullable `GenerationStatus`. The complete, executable callback example is in [Home.razor.cs](../samples/Marmotte.ChatUi.Sample/Components/Pages/Home.razor.cs). It awaits acceptance, starts a host-owned stream, replaces snapshots for each update, and cancels/awaits the stream on disposal. Adapt these callbacks to your own authorized use cases; the RCL does not register or call `IConversationService` itself. See the [core contracts](core.md).

## Public components

All types are in `Marmotte.ChatUi.Components`. Put standalone components inside `ChatTheme`; `ChatWidget` already supplies one. Every instance must have unique message/attachment IDs. Preserve message order in the supplied list.

| Component | Inputs and behavior |
| --- | --- |
| `ChatTheme` | `Theme` defaults to `System`; `Class`, `Style`, `ChildContent` define the host's theme boundary. |
| `ChatWidget` | `Title`, `Messages`, `Draft`/`DraftChanged`, `Theme`/`ThemeChanged`; `Status`, `Sending`, `Cancelling`, `Error`; `Attachments`; `OnSend`, `OnCancel`, `OnRemoveAttachment`. `ShowThemeSelector` defaults to true. `Class`/`Style` customize the root. |
| `MessageList` | `Messages`, accessible `Label`, `EmptyContent`, `MessageTemplate`, `AttachmentTemplate`. A focusable scroll region with an ordered list; no auto-scroll or live token announcements. |
| `ChatMessage` | Required `Message`, optional `ContentTemplate` and `AttachmentTemplate`. Encoded plain text, speaker, UTC time, preparation metadata, and partial-response state. |
| `MessageComposer` | `Draft`/`DraftChanged`, `OnSend`, `Busy`, `BlockedReason`. Multiline text with explicit send, local duplicate guard and safe failure feedback. |
| `GenerationIndicator` | `Status`, `Sending`, `Cancelling`, `Error`. A polite atomic live region plus an alert for safe errors. |
| `AttachmentPreview` | Required `Attachment`, `PreviewTemplate`, `OnRemove`, `Disabled`. Metadata and polite preparation announcements; no URLs, fetching, upload or assumed model support. Omit removal for history. |

`ChatWidget` offers `Branding`, `EmptyContent`, `Footer`, `RenderFragment<MessageView> MessageTemplate`, and `RenderFragment<AttachmentView> AttachmentTemplate`. Message templates replace content only, preserving speaker and state. The default widget has no mascot or branding; both Web and the sample supply the preserved mascot themselves. Templates, `Class`, and `Style` are trusted host input. The host must sanitize any custom HTML/Markdown renderer and authorize attachment retrieval; it also owns object URLs and their cleanup. The defaults never interpret model text or filenames as HTML, Markdown, CSS, or links.

## Callback and state contract

- Bind both `Draft` and `DraftChanged`. `OnSend(string)` receives the exact nonblank draft, including newlines. It returns a `Task` when acceptance succeeds, **not when the entire response completes**. Only successful acceptance clears the draft. Exceptions preserve it; `ChatOperationException` exposes its safe category message, cancellation has dedicated feedback, and other failures show a generic message without logging their contents. The component awaits callbacks and blocks duplicate submission while awaiting them.
- The host assigns submission IDs, handles authorization and idempotency, sets `Sending` during acceptance, and supplies the latest `Status`. `Pending`/`Streaming`, `Sending`, and `Cancelling` block new submissions. Host callbacks own deadlines and `CancellationTokenSource` lifetimes; components neither start nor dispose application operations.
- `OnCancel()` is awaited once at a time and shows “Stopping response…”. The host must actually cancel work and publish `Cancelled` after acknowledgement. Failed cancellation exposes a safe error without declaring success. Partial text remains in the message snapshots. Stopping a stream alone is not durable cancellation; use the appropriate application operation in an integrated host.
- `ThemeChanged(ThemePreference)` requests a choice; the host updates `Theme` and decides whether to persist it with preferences. No local storage or global browser state is modified. `OnRemoveAttachment(Guid)` asks the host to remove pending metadata. The host then replaces `Attachments`.
- Pending attachments always block this **text-only** composer, including `Ready` attachments. Readiness means preparation completed, not provider support or delivery. Removal is available before sending; actual uploads/vision arrive in M9.
- After sending succeeds or fails, focus returns to the composer. Enter inserts a newline; Tab reaches Send message, and Enter/Space activates the button. There is no global keyboard handler, auto-focus, or automatic scrolling during streaming.

Actions are disabled during static SSR/prerendering. Missing callbacks also disable the associated controls. Defaults are English; arbitrary label localization and a Markdown renderer are not implemented. Templates can replace content, but a full localization API remains future work.

## Semantic tokens and themes

All selectors are scoped under `.mcui-theme`; the stylesheet does not select `body`, `html`, or unrelated host controls. `Light` and `Dark` select explicit palettes; `System` reacts to `prefers-color-scheme`, including live OS changes, without JavaScript. Private `--_mcui-*` palette defaults reset at each theme boundary. Public `--mcui-*` properties are **not** reset, so overrides inherit from the application or a surrounding container and survive theme changes. Do not rely on private tokens.

| Public tokens (each name has prefix `--mcui-`) | Defaults / purpose |
| --- | --- |
| `surface`, `surface-muted`, `surface-user` | Theme-aware main, assistant, and user surfaces |
| `text`, `text-muted` | Main and secondary text |
| `accent`, `accent-hover`, `on-accent` | Action background, hover, contrasting text |
| `border`, `focus`, `error`, `success` | Control boundaries and semantic states |
| `space-1` … `space-6` | `0.25rem`, `0.5rem`, `0.75rem`, `1rem`, `1.5rem`, `2rem` |
| `font-family`, `font-size`, `font-small`, `font-heading` | `system-ui, sans-serif`, `1rem`, `0.875rem`, `1.25rem` |
| `font-weight-strong`, `line-height` | `650`, `1.6` |
| `radius-sm`, `radius-md`, `radius-lg`, `shadow` | `0.5rem`, `0.875rem`, `1.25rem`, theme-aware shadow |
| `focus-width`, `focus-offset`, `control-height`, `disabled-opacity` | `3px`, `3px`, `2.75rem`, `0.6` |
| `transcript-min-height`, `transcript-max-height` | `18rem`, `32rem`; scrolling stays within the transcript |

Application and instance overrides can coexist:

```css
/* Host stylesheet; inherited by all widgets below this container. */
.my-application {
    --mcui-font-family: "Atkinson Hyperlegible", system-ui, sans-serif;
    --mcui-radius-lg: 0.75rem;
}
```

```razor
<ChatWidget Title="Custom palette" Theme="ThemePreference.Light"
            Style="--mcui-accent: #6440a0; --mcui-accent-hover: #4e2d83; --mcui-on-accent: #ffffff;" />
```

Override related colors together and recheck contrast in every supported theme. Public overrides intentionally take precedence even in dark mode. Library defaults provide visible focus, native labels and controls, 44px minimum controls, wrapping content, narrow-screen spacing, and forced-color disabled text. The transcript is keyboard-scrollable; status changes are separate from content so token updates do not repeatedly announce the conversation.

## Verification and boundaries

bUnit tests cover encoding, partial content, callbacks, duplicate sends, cancellation, draft retention, attachment blocking, labels, templates, independent themes, and disabled SSR actions. Architecture tests enforce the sample's independence. The browser suite runs the actual Interactive Server sample: keyboard/focus, sending/streaming/cancellation/failure, theme/OS changes, inherited and instance tokens, 360px/1280px layouts, and axe accessibility checks. See [development](development.md) for commands and validation limits. Automated checks are not a screen-reader usability audit.

Static SSR and Interactive Server are verified. WebAssembly and Auto retain browser-compatible dependencies but have no dedicated host/consumption validation. Full authenticated model/history orchestration is M8, uploads M9, and NuGet distribution M10. No package publication, production deployment, or cross-browser compatibility claim is made.
