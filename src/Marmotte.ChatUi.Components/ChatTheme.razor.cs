using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;

namespace Marmotte.ChatUi.Components;

/// <summary>Scopes the design system to its children, inheriting host token overrides without changing document styles.</summary>
public partial class ChatTheme
{
    /// <summary>Gets or sets the palette; System follows the browser's color-scheme preference without JavaScript.</summary>
    [Parameter] public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Gets or sets optional host CSS classes on this instance.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Gets or sets trusted host CSS, typically instance-specific custom properties. Never use model output here.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Gets or sets the content sharing this theme.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
