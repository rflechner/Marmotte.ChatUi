using System.Net;
using System.Security.Claims;
using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Core.Tests;
using Marmotte.ChatUi.Web.Identity;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Verifies OIDC protocol validation, session lifecycle, route protection, and application isolation through the real current-user adapter.</summary>
public sealed class IdentityTests
{
    [Theory]
    [InlineData("Production", "http://localhost/realms/test")]
    [InlineData("Development", "http://remote.example/realms/test")]
    [InlineData("Development", "https://name:secret@identity.example/realm")]
    [InlineData("Development", "https://identity.example/realm?secret=value")]
    [InlineData("Development", "https://identity.example/realm#fragment")]
    public void Unsafe_authorities_fail_at_startup(string environment, string authority)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration["Authentication:Authority"] = authority;
        builder.Configuration["Authentication:ClientId"] = "test";
        builder.Configuration["Authentication:ClientSecret"] = "private-client-secret";
        var error = Assert.Throws<InvalidOperationException>(() => builder.AddHostIdentity());
        Assert.DoesNotContain("private-client-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_discovery_returns_a_generic_public_error()
    {
        await using var original = new OidcTestHost();
        await using var host = original.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
                options => options.ConfigurationManager = new UnavailableDiscovery())));
        using var browser = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var response = await browser.GetAsync("/account/login", TestContext.Current.CancellationToken);
        Assert.Equal("/account/error", response.Headers.Location?.OriginalString);
        using var page = await browser.GetAsync("/account/error?error_description=private-provider-detail", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain("private-provider-detail", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_remote_logout_still_revokes_the_local_session()
    {
        await using var host = new OidcTestHost();
        using var browser = host.Browser();
        var (principal, _) = await host.LoginAsync(browser, "alice");
        var token = await OidcTestHost.LogoutTokenAsync(browser);
        host.Provider.FailLogout = true;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        using var response = await browser.PostAsync("/account/logout", form, TestContext.Current.CancellationToken);
        Assert.Equal("/account/error", response.Headers.Location?.OriginalString);
        Assert.Null(host.Services.GetRequiredService<AuthenticationSessionStore>().GetUserId(principal));
        using var page = await browser.GetAsync("/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [Theory]
    [InlineData("/workspace", "GET")]
    [InlineData("/_blazor/negotiate?negotiateVersion=1", "POST")]
    [InlineData("/account/logout", "POST")]
    public async Task Anonymous_requests_cannot_reach_protected_entry_points(string path, string method)
    {
        await using var host = new OidcTestHost();
        using var browser = host.Browser();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);
        if (path.StartsWith("/_blazor", StringComparison.Ordinal))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/account/login", response.Headers.Location?.AbsolutePath);
        }
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("nonce")]
    [InlineData("expired")]
    [InlineData("subject")]
    public async Task Invalid_tokens_fail_without_exposing_provider_details(string invalidClaim)
    {
        await using var host = new OidcTestHost();
        host.Provider.InvalidClaim = invalidClaim;
        using var browser = host.Browser();
        using var challenge = await browser.GetAsync("/account/login", TestContext.Current.CancellationToken);
        var query = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        host.Provider.Nonce = query["nonce"].ToString();
        using var response = await browser.GetAsync(QueryHelpers.AddQueryString("/signin-oidc", new Dictionary<string, string?>
        {
            ["state"] = query["state"].ToString(),
            ["code"] = "alice"
        }), TestContext.Current.CancellationToken);
        Assert.Equal("/account/error", response.Headers.Location?.OriginalString);
        using var protectedPage = await browser.GetAsync("/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
    }

    [Fact]
    public async Task Missing_correlation_or_tampered_state_does_not_sign_in()
    {
        await using var host = new OidcTestHost();
        using var browser = host.Browser();
        using var response = await browser.GetAsync("/signin-oidc?code=alice&state=forged", TestContext.Current.CancellationToken);
        Assert.Equal("/account/error", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_requires_antiforgery_revokes_existing_circuits_and_preserves_other_users()
    {
        await using var host = new OidcTestHost();
        using var alice = host.Browser();
        using var bob = host.Browser();
        var (alicePrincipal, cookie) = await host.LoginAsync(alice, "alice");
        await host.LoginAsync(bob, "bob");
        using var scope = host.Services.CreateScope();
        var current = SetCircuitIdentity(scope, alicePrincipal);
        Assert.NotNull(await current.GetUserIdAsync(TestContext.Current.CancellationToken));
        using var getLogout = await alice.GetAsync("/account/logout", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getLogout.StatusCode);
        using var missingToken = await alice.PostAsync("/account/logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        Assert.NotNull(await current.GetUserIdAsync(TestContext.Current.CancellationToken));
        var token = await OidcTestHost.LogoutTokenAsync(alice);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        using var logout = await alice.PostAsync("/account/logout", form, TestContext.Current.CancellationToken);
        Assert.Equal($"{OidcTestHost.Issuer}/logout", logout.Headers.Location?.GetLeftPart(UriPartial.Path));
        Assert.NotEmpty(QueryHelpers.ParseQuery(logout.Headers.Location!.Query)["id_token_hint"].ToString());
        Assert.Null(await current.GetUserIdAsync(TestContext.Current.CancellationToken));
        using var replay = host.Browser();
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        using var rejected = await replay.GetAsync("/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        using var bobPage = await bob.GetAsync("/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, bobPage.StatusCode);
    }

    [Fact]
    public async Task Expiry_rejects_http_and_circuit_operations_without_waiting_for_UI_revalidation()
    {
        await using var host = new OidcTestHost();
        using var browser = host.Browser();
        var (principal, _) = await host.LoginAsync(browser, "alice");
        using var scope = host.Services.CreateScope();
        var current = SetCircuitIdentity(scope, principal);
        var service = new ConversationService(current, new TestStore(), host.Clock);
        await service.CreateAsync("Before expiry", TestContext.Current.CancellationToken);
        host.Clock.Now = host.Clock.Now.AddHours(1);
        await ErrorAsync(ChatErrorCode.Unauthenticated, () => service.ListAsync(new PageRequest(), TestContext.Current.CancellationToken));
        using var response = await browser.GetAsync("/workspace", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current.GetUserIdAsync(cancellation.Token).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_OIDC_users_cannot_read_modify_delete_or_select_each_others_data(bool faultyRepository)
    {
        await using var host = new OidcTestHost();
        using var alice = host.Browser();
        using var bob = host.Browser();
        var (alicePrincipal, _) = await host.LoginAsync(alice, "alice");
        var (bobPrincipal, _) = await host.LoginAsync(bob, "bob");
        using var scope = host.Services.CreateScope();
        var identity = SetCircuitIdentity(scope, alicePrincipal);
        var aliceId = await identity.GetUserIdAsync(TestContext.Current.CancellationToken);
        var store = new TestStore();
        var conversations = new ConversationService(identity, store, host.Clock);
        var profiles = new ConnectionProfileService(identity, store, host.Clock);
        var preferences = new UserPreferencesService(identity, store, store);
        var conversation = await conversations.CreateAsync("Private", TestContext.Current.CancellationToken);
        var request = new SaveProfileRequest("Local", new Uri("http://localhost/model"), "model");
        var profile = await profiles.CreateAsync(request, TestContext.Current.CancellationToken);
        await preferences.SaveAsync(new PreferencesView(ThemePreference.Dark, profile.Id, null), TestContext.Current.CancellationToken);
        SetCircuitIdentity(scope, bobPrincipal);
        Assert.NotEqual(aliceId, await identity.GetUserIdAsync(TestContext.Current.CancellationToken));
        store.ReturnForeignData = faultyRepository;
        await ErrorAsync(ChatErrorCode.NotFound, () => conversations.LoadAsync(conversation.Summary.Id, TestContext.Current.CancellationToken));
        await ErrorAsync(ChatErrorCode.NotFound, () => conversations.DeleteAsync(conversation.Summary.Id, TestContext.Current.CancellationToken));
        await ErrorAsync(ChatErrorCode.NotFound, () => conversations.RecordMessageAsync(conversation.Summary.Id, new RecordMessageRequest(Guid.NewGuid(), "Injected"), TestContext.Current.CancellationToken));
        await ErrorAsync(ChatErrorCode.NotFound, () => profiles.LoadAsync(profile.Id, TestContext.Current.CancellationToken));
        await ErrorAsync(ChatErrorCode.NotFound, () => profiles.UpdateAsync(profile.Id, request, TestContext.Current.CancellationToken));
        await ErrorAsync(ChatErrorCode.NotFound, () => profiles.DeleteAsync(profile.Id, TestContext.Current.CancellationToken));
        await ErrorAsync(ChatErrorCode.NotFound, () => preferences.SaveAsync(new PreferencesView(ThemePreference.Light, profile.Id, null), TestContext.Current.CancellationToken));
        if (faultyRepository)
        {
            await ErrorAsync(ChatErrorCode.NotFound, () => conversations.ListAsync(new PageRequest(), TestContext.Current.CancellationToken));
            await ErrorAsync(ChatErrorCode.NotFound, () => profiles.ListAsync(new PageRequest(), TestContext.Current.CancellationToken));
            await ErrorAsync(ChatErrorCode.NotFound, () => preferences.LoadAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Empty((await conversations.ListAsync(new PageRequest(), TestContext.Current.CancellationToken)).Items);
            Assert.Empty((await profiles.ListAsync(new PageRequest(), TestContext.Current.CancellationToken)).Items);
            Assert.Equal(ThemePreference.System, (await preferences.LoadAsync(TestContext.Current.CancellationToken)).Theme);
        }

        SetCircuitIdentity(scope, alicePrincipal);
        Assert.Equal(conversation, await conversations.LoadAsync(conversation.Summary.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Identity_mapping_uses_exact_issuer_subject_pairs_and_rejects_ambiguous_claims()
    {
        var principal = Principal("issuer:a", "b", "old@example.test");
        var id = OidcIdentity.Map(principal);
        Assert.NotNull(id);
        Assert.Equal("oidc-v1:1db2979df50a1bae3f9b0faeb9de6118969264d85ec96f510cab900005e1a7b2", id.Value);
        Assert.Equal(id, OidcIdentity.Map(Principal("issuer:a", "b", "new@example.test")));
        Assert.NotEqual(id, OidcIdentity.Map(Principal("issuer", "a:b", "old@example.test")));
        Assert.NotEqual(id, OidcIdentity.Map(Principal("issuer:a", "B", "old@example.test")));
        Assert.Null(OidcIdentity.Map(new ClaimsPrincipal(new ClaimsIdentity(principal.Claims))));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("sub", "other"));
        Assert.Null(OidcIdentity.Map(principal));
    }

    /// <summary>Models the framework supplying a trusted principal to a scoped circuit, while retaining the production current-user adapter.</summary>
    private static ICurrentUser SetCircuitIdentity(IServiceScope scope, ClaimsPrincipal principal)
    {
        var state = (ServerAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        state.SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));
        return scope.ServiceProvider.GetRequiredService<ICurrentUser>();
    }

    /// <summary>Builds already-authenticated claims for pure mapping tests, without creating an authorized host session.</summary>
    private static ClaimsPrincipal Principal(string issuer, string subject, string email) => new(new ClaimsIdentity(
        [new Claim("iss", issuer), new Claim("sub", subject), new Claim("email", email)], "test"));

    /// <summary>Checks the public error contract without depending on private exception messages.</summary>
    private static async Task ErrorAsync(ChatErrorCode code, Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<ChatOperationException>(operation);
        Assert.Equal(code, error.Code);
    }

    /// <summary>Simulates discovery failure without a real network call or exposing its private diagnostic to the browser.</summary>
    private sealed class UnavailableDiscovery : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromException<OpenIdConnectConfiguration>(new HttpRequestException("private-provider-detail"));

        public void RequestRefresh() { }
    }
}
