using Microsoft.AspNetCore.Authorization;

namespace Marmotte.ChatUi.Web.Identity;

/// <summary>Requires an active server session in addition to an authenticated principal.</summary>
internal sealed class ActiveSessionRequirement : IAuthorizationRequirement;

/// <summary>Applies the same expiry and revocation checks to protected endpoints and Blazor route authorization.</summary>
internal sealed class ActiveSessionHandler(AuthenticationSessionStore sessions) : AuthorizationHandler<ActiveSessionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveSessionRequirement requirement)
    {
        if (sessions.GetUserId(context.User) is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
