using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Npgsql;

namespace Marmotte.ChatUi.Infrastructure.Tests;

/// <summary>Exercises the real AppHost on an isolated Docker volume, then restarts it to verify retained data and repeatable initialization.</summary>
public sealed class InfrastructureTests
{
    /// <summary>Enables container checks explicitly; routine core/host tests require neither Docker nor external credentials.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("MARMOTTE_RUN_INFRASTRUCTURE_TESTS") == "1";

    [Fact(Skip = "Set MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 with Docker running.", SkipUnless = nameof(Enabled))]
    public async Task AppHost_initializes_and_restarts_without_losing_application_or_identity_data()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var cancellationToken = timeout.Token;
        var volumeName = $"marmotte-infrastructure-test-{Guid.NewGuid():N}";
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
                await using var app = await builder.BuildAsync(cancellationToken);
                await app.StartAsync(cancellationToken);
                await app.ResourceNotifications.WaitForResourceHealthyAsync("web", cancellationToken);

                using var web = app.CreateHttpClient("web", "http");
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

                await using var identity = new NpgsqlConnection(await app.GetConnectionStringAsync("keycloakdb", cancellationToken));
                await identity.OpenAsync(cancellationToken);
                Assert.Equal("marmotte_identity", await ExecuteScalarAsync(identity, "SELECT current_database()", cancellationToken));
                Assert.Equal(0L, await ExecuteScalarAsync(identity, "SELECT count(*) FROM pg_extension WHERE extname = 'vector'", cancellationToken));

                using var keycloak = app.CreateHttpClient("keycloak", "http");
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

                await chat.CloseAsync();
                await identity.CloseAsync();
                NpgsqlConnection.ClearAllPools();
                await app.StopAsync(cancellationToken);
            }
        }
        finally
        {
            await RemoveTestVolumeAsync(volumeName);
        }
    }

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
