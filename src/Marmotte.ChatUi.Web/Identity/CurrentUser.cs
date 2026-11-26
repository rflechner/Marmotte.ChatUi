using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Domain;
using Microsoft.AspNetCore.Components.Authorization;

namespace Marmotte.ChatUi.Web.Identity;

/// <summary>Resolves the current HTTP or circuit identity on each use-case call and rejects revoked or expired sessions immediately.</summary>
internal sealed class CurrentUser(AuthenticationStateProvider authenticationState, AuthenticationSessionStore sessions) : ICurrentUser
{
    /// <inheritdoc />
    public async ValueTask<UserId?> GetUserIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authenticationState.GetAuthenticationStateAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return sessions.GetUserId(state.User);
    }
}
