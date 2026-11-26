using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Npgsql;

namespace Marmotte.ChatUi.Infrastructure.Tests;

/// <summary>Exercises the real AppHost on an isolated Docker volume, then restarts it to verify retained data and repeatable initialization.</summary>
public sealed partial class InfrastructureTests
{
    /// <summary>Enables container checks explicitly; routine core/host tests require neither Docker nor external credentials.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("MARMOTTE_RUN_INFRASTRUCTURE_TESTS") == "1";

    [Fact(Skip = "Set MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 with Docker running.", SkipUnless = nameof(Enabled))]
    public async Task AppHost_initializes_and_restarts_without_losing_application_or_identity_data()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(BrowserEnabled ? 10 : 5));
        var cancellationToken = timeout.Token;
        var volumeName = $"marmotte-infrastructure-test-{Guid.NewGuid():N}";
        var keyDirectory = Path.Combine(Path.GetTempPath(), $"marmotte-infrastructure-keys-{Guid.NewGuid():N}");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var webPort = GetAvailablePort();
        var keycloakPort = GetAvailablePort();
        while (keycloakPort == webPort)
        {
            keycloakPort = GetAvailablePort();
        }

        var pgAdminPort = GetAvailablePort();
        while (pgAdminPort == webPort || pgAdminPort == keycloakPort)
        {
            pgAdminPort = GetAvailablePort();
        }

        string[] arguments =
        [
            $"--Infrastructure:PostgresVolume={volumeName}",
            $"--Infrastructure:KeyDirectory={keyDirectory}",
            $"--Infrastructure:WebPort={webPort.ToString(CultureInfo.InvariantCulture)}",
            $"--Infrastructure:KeycloakPort={keycloakPort.ToString(CultureInfo.InvariantCulture)}",
            $"--Infrastructure:PgAdminPort={pgAdminPort.ToString(CultureInfo.InvariantCulture)}",
            $"--Parameters:postgres-password={password}",
            $"--Parameters:keycloak-password={password}",
            $"--Parameters:keycloak-client-secret={password}",
            "--DcpPublisher:RandomizePorts=false"
        ];

        try
        {
            for (var start = 0; start < 3; start++)
            {
                var options = new DistributedApplicationTestingBuilderOptions { EnableDashboard = true };
                await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Marmotte_ChatUi_AppHost>(options, arguments, cancellationToken);
                // Dashboard testing defaults are applied after arguments; preserve the already-randomized public OIDC origins.
                builder.Configuration["DcpPublisher:RandomizePorts"] = "false";
                await using var app = await builder.BuildAsync(cancellationToken);
                await app.StartAsync(cancellationToken);
                await app.ResourceNotifications.WaitForResourceHealthyAsync("web", cancellationToken);

                using var web = app.CreateHttpClient("web", "http");
                Assert.Equal(webPort, web.BaseAddress!.Port);
                using var health = await web.GetAsync("/health", cancellationToken);
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);

                await app.ResourceNotifications.WaitForResourceHealthyAsync("pgadmin", cancellationToken);
                using var pgAdmin = app.CreateHttpClient("pgadmin", "http");
                using var pgAdminResponse = await pgAdmin.GetAsync("/browser/", cancellationToken);
                Assert.Equal(HttpStatusCode.OK, pgAdminResponse.StatusCode);
                Assert.Contains("pgAdmin", await pgAdminResponse.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);

                await using var chat = new NpgsqlConnection(await app.GetConnectionStringAsync("chatdb", cancellationToken));
                await chat.OpenAsync(cancellationToken);
                Assert.Equal("0.8.7", await ExecuteScalarAsync(chat, "SELECT extversion FROM pg_extension WHERE extname = 'vector'", cancellationToken));
                Assert.Equal("marmotte_chat", await ExecuteScalarAsync(chat, "SELECT current_database()", cancellationToken));
                Assert.Equal(2L, await ExecuteScalarAsync(chat, "SELECT count(*) FROM chat.\"__EFMigrationsHistory\"", cancellationToken));

                await using var identity = new NpgsqlConnection(await app.GetConnectionStringAsync("keycloakdb", cancellationToken));
                await identity.OpenAsync(cancellationToken);
                Assert.Equal("marmotte_identity", await ExecuteScalarAsync(identity, "SELECT current_database()", cancellationToken));
                Assert.Equal(0L, await ExecuteScalarAsync(identity, "SELECT count(*) FROM pg_extension WHERE extname = 'vector'", cancellationToken));

                using var keycloak = app.CreateHttpClient("keycloak", "http");
                Assert.Equal(keycloakPort, keycloak.BaseAddress!.Port);
                using var discoveryResponse = await keycloak.GetAsync("/realms/marmotte/.well-known/openid-configuration", cancellationToken);
                discoveryResponse.EnsureSuccessStatusCode();
                using var discovery = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync(cancellationToken));
                Assert.Equal($"http://localhost:{keycloakPort}/realms/marmotte", discovery.RootElement.GetProperty("issuer").GetString());

                await AuthenticateAdminAsync(keycloak, password, cancellationToken);
                await VerifyClientAsync(keycloak, webPort, cancellationToken);

                if (start == 0)
                {
                    await ExecuteScalarAsync(chat, "CREATE TABLE m3_restart_probe (value text NOT NULL); INSERT INTO m3_restart_probe VALUES ('retained');", cancellationToken);
                    using var createUser = await keycloak.PostAsJsonAsync("/admin/realms/marmotte/users",
                        new { username = "restart-probe", enabled = true }, cancellationToken);
                    Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);
                    await VerifyOidcAsync(keycloak, new Uri($"http://localhost:{webPort}"), password, cancellationToken);
                }
                else
                {
                    Assert.Equal("retained", await ExecuteScalarAsync(chat, "SELECT value FROM m3_restart_probe", cancellationToken));
                    using var usersResponse = await keycloak.GetAsync("/admin/realms/marmotte/users?username=restart-probe&exact=true", cancellationToken);
                    usersResponse.EnsureSuccessStatusCode();
                    using var users = JsonDocument.Parse(await usersResponse.Content.ReadAsStringAsync(cancellationToken));
                    Assert.Single(users.RootElement.EnumerateArray());
                    Assert.Equal("restart-probe", users.RootElement[0].GetProperty("username").GetString());
                }

                if (start == 1)
                {
                    // The third start must repair a pre-existing database without resetting its data.
                    await ExecuteScalarAsync(chat, "DROP EXTENSION vector", cancellationToken);
                }

                if (BrowserEnabled && start < 2)
                {
                    await VerifyWorkspaceBrowserAsync(webPort, password, start == 0 ? "initial" : "restart", cancellationToken);
                }

                await chat.CloseAsync();
                await identity.CloseAsync();
                NpgsqlConnection.ClearAllPools();
                await app.StopAsync(cancellationToken);
            }
        }
        finally
        {
            await RemoveTestVolumeAsync(volumeName);
            if (Directory.Exists(keyDirectory))
            {
                Directory.Delete(keyDirectory, recursive: true);
            }
        }
    }

    private static bool BrowserEnabled => Environment.GetEnvironmentVariable("MARMOTTE_RUN_BROWSER_TESTS") == "1";

    /// <summary>Runs real browser circuits against isolated Aspire resources, passing temporary credentials only in the environment.</summary>
    private static async Task VerifyWorkspaceBrowserAsync(int port, string password, string phase, CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Marmotte.ChatUi.slnx")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("tests/browser/workspace.mjs");
        start.Environment["MARMOTTE_TEST_WEB_URL"] = $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";
        start.Environment["MARMOTTE_TEST_PASSWORD"] = password;
        start.Environment["MARMOTTE_TEST_PHASE"] = phase;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start browser checks.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var diagnostics = $"{await output}\n{await error}";
            foreach (var secret in new[] { password, Environment.GetEnvironmentVariable("MARMOTTE_MODEL_KEY"), Environment.GetEnvironmentVariable("MARMOTTE_UNSLOTH_KEY") })
            {
                if (!string.IsNullOrEmpty(secret))
                {
                    diagnostics = diagnostics.Replace(secret, "[redacted]", StringComparison.Ordinal);
                }
            }

            Assert.True(process.ExitCode == 0, $"Browser checks failed: {diagnostics}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>Signs two independent browser sessions into real Keycloak and verifies protected routes, antiforgery, RP logout, and cookie revocation.</summary>
    private static async Task VerifyOidcAsync(HttpClient admin, Uri webOrigin, string password, CancellationToken cancellationToken)
    {
        foreach (var username in new[] { "m4-alice", "m4-bob" })
        {
            using var created = await admin.PostAsJsonAsync("/admin/realms/marmotte/users", new
            {
                username,
                firstName = username,
                lastName = "Test",
                email = $"{username}@example.test",
                emailVerified = true,
                enabled = true,
                credentials = new[] { new { type = "password", value = password, temporary = false } }
            }, cancellationToken);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var aliceHandler = new HttpClientHandler();
        using var alice = new HttpClient(aliceHandler) { BaseAddress = webOrigin };
        using var bobHandler = new HttpClientHandler();
        using var bob = new HttpClient(bobHandler) { BaseAddress = webOrigin };
        // Keycloak treats localhost as secure for browsers, but HttpClient rejects Secure cookies over HTTP.
        // Its documented Safari compatibility path emits usable loopback HTTP cookies without changing realm security.
        const string safari = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";
        alice.DefaultRequestHeaders.UserAgent.ParseAdd(safari);
        bob.DefaultRequestHeaders.UserAgent.ParseAdd(safari);
        await SignInAsync(alice, "m4-alice", password, cancellationToken);
        await SignInAsync(bob, "m4-bob", password, cancellationToken);
        var oldCookie = aliceHandler.CookieContainer.GetCookieHeader(webOrigin);
        using var page = await alice.GetAsync("/workspace", cancellationToken);
        var html = await page.Content.ReadAsStringAsync(cancellationToken);
        var token = AntiforgeryInput().Match(html);
        Assert.True(token.Success);
        using var missingToken = await alice.PostAsync("/account/logout", null, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
        });
        using var logout = await alice.PostAsync("/account/logout", form, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal("/", logout.RequestMessage?.RequestUri?.AbsolutePath);
        using var loginAgain = await alice.GetAsync("/account/login", cancellationToken);
        Assert.Contains("kc-form-login", await loginAgain.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        using var replayHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var replay = new HttpClient(replayHandler) { BaseAddress = webOrigin };
        replay.DefaultRequestHeaders.Add("Cookie", oldCookie);
        using var denied = await replay.GetAsync("/workspace", cancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        using var bobPage = await bob.GetAsync("/workspace", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, bobPage.StatusCode);
        Assert.Contains("m4-bob", await bobPage.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    /// <summary>Submits Keycloak's real login form and follows the authorization-code callback using a private cookie jar.</summary>
    private static async Task SignInAsync(HttpClient browser, string username, string password, CancellationToken cancellationToken)
    {
        using var login = await browser.GetAsync("/account/login", cancellationToken);
        var html = await login.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(login.IsSuccessStatusCode, $"Login returned {login.StatusCode} at {login.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path)}.");
        Assert.NotEqual("/account/error", login.RequestMessage?.RequestUri?.AbsolutePath);
        var action = LoginFormAction().Match(html);
        Assert.True(action.Success, "Keycloak must present the login form.");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["credentialId"] = string.Empty
        });
        using var signedIn = await browser.PostAsync(WebUtility.HtmlDecode(action.Groups[1].Value), form, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal("/workspace", signedIn.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Contains(username, await signedIn.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [GeneratedRegex("<form\\b[^>]*\\baction=\"([^\"]+)\"")]
    private static partial Regex LoginFormAction();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInput();

    /// <summary>Obtains a free loopback port; Aspire subsequently binds it for this test's public origins.</summary>
    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Runs fixed test SQL with a bounded command timeout; the caller owns the open connection.</summary>
    private static async Task<object?> ExecuteScalarAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 15 };
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>Authenticates the temporary bootstrap administrator without printing credentials or tokens.</summary>
    private static async Task AuthenticateAdminAsync(HttpClient client, string password, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = "admin",
            ["password"] = password
        });
        using var response = await client.PostAsync("/realms/master/protocol/openid-connect/token", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());
    }

    /// <summary>Checks imported redirect restrictions and PKCE through Keycloak's admin API on every start.</summary>
    private static async Task VerifyClientAsync(HttpClient client, int webPort, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/admin/realms/marmotte/clients?clientId=marmotte-web", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var clients = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Single(clients.RootElement.EnumerateArray());
        var configuration = clients.RootElement[0];
        Assert.False(configuration.GetProperty("publicClient").GetBoolean());
        Assert.False(configuration.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.False(configuration.GetProperty("implicitFlowEnabled").GetBoolean());
        Assert.Equal("S256", configuration.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
        Assert.Equal($"http://localhost:{webPort}/signin-oidc", Assert.Single(configuration.GetProperty("redirectUris").EnumerateArray()).GetString());
        Assert.Equal($"http://localhost:{webPort}/signout-callback-oidc", configuration.GetProperty("attributes").GetProperty("post.logout.redirect.uris").GetString());
    }

    /// <summary>Removes only this test's randomly named volume after Aspire disposes its containers, even if assertions fail.</summary>
    /// <remarks>Cleanup has its own deadline so test cancellation does not leave a volume behind; failures are surfaced.</remarks>
    private static async Task RemoveTestVolumeAsync(string volumeName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startInfo = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        startInfo.ArgumentList.Add("volume");
        startInfo.ArgumentList.Add("rm");
        startInfo.ArgumentList.Add("--force");
        startInfo.ArgumentList.Add(volumeName);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start Docker volume cleanup.");
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, "Docker could not remove the isolated infrastructure test volume.");
    }
}
