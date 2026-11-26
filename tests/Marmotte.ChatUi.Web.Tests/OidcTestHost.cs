using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Marmotte.ChatUi.Core.Tests;
using Marmotte.ChatUi.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Runs the actual host and OIDC/cookie handlers with signed test tokens, isolated cookies, an ephemeral key ring, and a controlled clock.</summary>
internal sealed partial class OidcTestHost : WebApplicationFactory<Program>
{
    internal const string Issuer = "https://identity.example/realms/test";
    internal TestClock Clock { get; } = new() { Now = DateTimeOffset.UtcNow };
    internal TestIdentityProvider Provider { get; } = new();

    /// <summary>Configures a confidential client and substitutes only the remote protocol server, never the host authentication handlers.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:Authority", Issuer);
        builder.UseSetting("Authentication:ClientId", "test-web");
        builder.UseSetting("Authentication:ClientSecret", "test-client-secret");
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = Issuer,
                    AuthorizationEndpoint = $"{Issuer}/authorize",
                    TokenEndpoint = $"{Issuer}/token",
                    EndSessionEndpoint = $"{Issuer}/logout"
                };
                options.Configuration.SigningKeys.Add(Provider.SigningKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                options.Backchannel = new HttpClient(Provider, disposeHandler: false);
                options.Events.OnRedirectToIdentityProviderForSignOut = _ => Provider.FailLogout
                    ? Task.FromException(new HttpRequestException("private-provider-detail")) : Task.CompletedTask;
            });
        });
    }

    /// <summary>Creates an independent browser cookie jar and exposes redirects for protocol assertions.</summary>
    internal HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    /// <summary>Executes a complete authorization-code callback including correlation, nonce, signature, audience, and issuer validation.</summary>
    internal async Task<(ClaimsPrincipal Principal, string Cookie)> LoginAsync(HttpClient browser, string subject)
    {
        using var challenge = await browser.GetAsync("/account/login?returnUrl=https://untrusted.example", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var query = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.NotEmpty(query["code_challenge"].ToString());
        Provider.Nonce = query["nonce"].ToString();
        var callback = QueryHelpers.AddQueryString("/signin-oidc", new Dictionary<string, string?>
        {
            ["code"] = subject,
            ["state"] = query["state"].ToString()
        });
        using var response = await browser.GetAsync(callback, TestContext.Current.CancellationToken);
        Assert.Equal("/workspace", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("Marmotte.Session=", StringComparison.Ordinal)).Split(';')[0];
        Assert.Contains("httponly", response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Marmotte.Session=", StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var browserTicket = options.TicketDataFormat.Unprotect(cookie["Marmotte.Session=".Length..]);
        Assert.NotNull(browserTicket);
        Assert.Empty(browserTicket.Properties.GetTokens());
        var key = Assert.Single(browserTicket.Principal.Claims).Value;
        var ticket = await Services.GetRequiredService<AuthenticationSessionStore>().RetrieveAsync(key);
        Assert.NotNull(ticket);
        Assert.DoesNotContain(ticket.Principal.Claims, claim => claim.Type == "ownerId" || claim.Value == "forged-session");
        Assert.Equal("id_token", Assert.Single(ticket.Properties.GetTokens()).Name);
        return (ticket.Principal, cookie);
    }

    /// <summary>Reads the real rendered logout form's antiforgery token for a full-page POST.</summary>
    internal static async Task<string> LogoutTokenAsync(HttpClient browser)
    {
        using var page = await browser.GetAsync("/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"type\":\"server\"", html, StringComparison.Ordinal);
        var match = AntiforgeryInput().Match(html);
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Disposes test-only signing material after the host and its clients are released.</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Provider.Dispose();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInput();
}

/// <summary>Acts as a deterministic OIDC token endpoint with real RSA signatures; never connects to an external identity service.</summary>
internal sealed class TestIdentityProvider : HttpMessageHandler
{
    private readonly RSA rsa = RSA.Create(2048);
    internal SecurityKey SigningKey => new RsaSecurityKey(rsa) { KeyId = "test-signing-key" };
    internal string Nonce { get; set; } = string.Empty;
    internal string? InvalidClaim { get; set; }
    internal bool FailLogout { get; set; }

    /// <summary>Requires a confidential code exchange and PKCE verifier, then returns a signed identity for the supplied test code.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("test-client-secret", form["client_secret"]);
        Assert.NotEmpty(form["code_verifier"].ToString());
        var claims = new Dictionary<string, object>
        {
            ["sub"] = form["code"].ToString(),
            ["name"] = form["code"].ToString(),
            ["nonce"] = InvalidClaim == "nonce" ? "wrong-nonce" : Nonce,
            ["email"] = "same-email@example.test",
            ["marmotte:session"] = "forged-session",
            ["ownerId"] = "alice"
        };
        if (InvalidClaim == "subject")
        {
            claims.Remove("sub");
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = InvalidClaim == "issuer" ? "https://wrong.example" : OidcTestHost.Issuer,
            Audience = InvalidClaim == "audience" ? "wrong-client" : "test-web",
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-2),
            NotBefore = DateTime.UtcNow.AddMinutes(-2),
            Expires = InvalidClaim == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256)
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id_token = token, access_token = "never-exposed-access-token", token_type = "Bearer", expires_in = 600 })
        };
    }

    /// <summary>Releases the ephemeral signing key owned by this handler.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            rsa.Dispose();
        }

        base.Dispose(disposing);
    }
}
