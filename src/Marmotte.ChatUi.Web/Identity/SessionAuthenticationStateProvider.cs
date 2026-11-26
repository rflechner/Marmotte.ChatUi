using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Marmotte.ChatUi.Web.Identity;

/// <summary>Seeds request scopes from the authenticated HTTP user and revalidates circuit UI state against server sessions.</summary>
/// <remarks>Blazor supplies the circuit principal through SetAuthenticationState. No HttpContext is retained in the circuit.</remarks>
internal sealed class SessionAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly AuthenticationSessionStore sessions;

    /// <summary>Initializes a scoped provider; the framework replaces the initial state when attaching a circuit.</summary>
    public SessionAuthenticationStateProvider(ILoggerFactory loggerFactory, IHttpContextAccessor accessor,
        AuthenticationSessionStore sessions) : base(loggerFactory)
    {
        this.sessions = sessions;
        SetAuthenticationState(Task.FromResult(new AuthenticationState(accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity()))));
    }

    /// <inheritdoc />
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(sessions.GetUserId(authenticationState.User) is not null);
    }
}
