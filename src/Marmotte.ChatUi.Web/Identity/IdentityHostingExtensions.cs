using System.Security.Claims;
using Marmotte.ChatUi.Application.Ports;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Marmotte.ChatUi.Web.Identity;

/// <summary>Composes host-only OIDC, session storage, authorization, and account endpoints without coupling the component package to identity.</summary>
internal static class IdentityHostingExtensions
{
    /// <summary>Registers fail-closed identity services; absent configuration permits only the public foundation page.</summary>
    /// <exception cref="InvalidOperationException">Partial configuration, unsafe authority, or an invalid session duration is supplied.</exception>
    internal static void AddHostIdentity(this WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["Authentication:Authority"];
        var clientId = builder.Configuration["Authentication:ClientId"];
        var clientSecret = builder.Configuration["Authentication:ClientSecret"];
        var configured = authority is not null || clientId is not null || clientSecret is not null;
        var development = builder.Environment.IsDevelopment();
        var minutes = builder.Configuration.GetValue("Authentication:SessionMinutes", 30);
        if (minutes is < 1 or > 480 || configured &&
            (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) ||
             !Uri.TryCreate(authority, UriKind.Absolute, out var uri) ||
             !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
             uri.Scheme != Uri.UriSchemeHttps && !(development && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException("Configure a complete OIDC client, an HTTPS authority (loopback HTTP only in Development), and a session duration of 1–480 minutes.");
        }

        builder.Services.AddSingleton(new IdentityAvailability(configured));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<AuthenticationSessionStore>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddSingleton<IAuthorizationHandler, ActiveSessionHandler>();
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new ActiveSessionRequirement()).Build();
        builder.Services.AddAuthorizationBuilder().SetDefaultPolicy(policy);

        var authentication = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "Marmotte.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.LoginPath = "/account/login";
                options.AccessDeniedPath = "/account/error";
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(minutes);
            });
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<AuthenticationSessionStore, TimeProvider>((options, sessions, clock) =>
            {
                options.SessionStore = sessions;
                options.TimeProvider = clock;
            });

        if (!configured)
        {
            return;
        }

        authentication.AddOpenIdConnect(options =>
        {
            options.Authority = authority;
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.RequireHttpsMetadata = !development || new Uri(authority!).Scheme == Uri.UriSchemeHttps;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.ClaimActions.Clear();
            options.SaveTokens = true;
            options.UseTokenLifetime = true;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.BackchannelTimeout = TimeSpan.FromSeconds(15);
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
            options.RemoteSignOutPath = PathString.Empty;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = options.CorrelationCookie.SecurePolicy =
                development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.TokenValidationParameters.ValidIssuer = authority;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            options.Events.OnTokenValidated = context =>
            {
                if (context.Principal is null || OidcIdentity.Map(context.Principal) is null ||
                    OidcIdentity.SingleClaim(context.Principal, "iss") != authority)
                {
                    context.Fail("The identity is incomplete.");
                    return Task.CompletedTask;
                }

                // Keep only validated identity/display claims. Provider roles and custom owner/session claims grant no authority.
                var principal = context.Principal;
                context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("iss", OidcIdentity.SingleClaim(principal, "iss")!),
                    new Claim("sub", OidcIdentity.SingleClaim(principal, "sub")!),
                    new Claim("name", principal.FindFirst("name")?.Value ?? principal.FindFirst("preferred_username")?.Value ?? "User")
                ], CookieAuthenticationDefaults.AuthenticationScheme, "name", "role"));
                return Task.CompletedTask;
            };
            options.Events.OnTicketReceived = context =>
            {
                var properties = context.Properties!;
                var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                var limit = clock.GetUtcNow().AddMinutes(minutes);
                if (properties.ExpiresUtc is null || properties.ExpiresUtc > limit)
                {
                    properties.ExpiresUtc = limit;
                }

                properties.IsPersistent = false;
                // Only the ID token needed for RP-initiated logout stays in the server-side ticket.
                properties.StoreTokens(properties.GetTokens().Where(token => token.Name == "id_token").ToArray());
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/account/error");
                return Task.CompletedTask;
            };
        });
    }

    /// <summary>Maps full-page sign-in and antiforgery-protected POST sign-out with fixed local destinations and generic failures.</summary>
    internal static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapGet("/account/login", async (HttpContext context, IdentityAvailability availability) =>
        {
            if (!availability.IsConfigured)
            {
                context.Response.Redirect("/account/error");
                return;
            }

            try
            {
                await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/workspace" });
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                context.Response.Redirect("/account/error");
            }
        }).AllowAnonymous();

        app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery, IdentityAvailability availability) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Authenticate before revocation so the OIDC handler can read this request's cached ID token for logout.
            await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            try
            {
                if (availability.IsConfigured)
                {
                    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/" });
                }
                else
                {
                    context.Response.Redirect("/");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                context.Response.Redirect("/account/error");
            }
        }).RequireAuthorization();
    }
}

/// <summary>Exposes only whether sign-in is configured to the host UI, without sharing client secrets or authentication options.</summary>
internal sealed record IdentityAvailability(bool IsConfigured);
