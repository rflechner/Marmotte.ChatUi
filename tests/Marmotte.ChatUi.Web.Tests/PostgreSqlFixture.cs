using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Marmotte.ChatUi.Persistence.PostgreSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Owns one disposable PostgreSQL container and private temporary key directory; never uses development data.</summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly string containerName = $"marmotte-m5-test-{Guid.NewGuid():N}";
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private bool started;

    /// <summary>Opt-in container tests share the infrastructure flag; ordinary test runs require no Docker.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("MARMOTTE_RUN_INFRASTRUCTURE_TESTS") == "1";
    internal string ConnectionString { get; private set; } = string.Empty;
    internal string KeyDirectory { get; } = Path.Combine(Path.GetTempPath(), $"marmotte-m5-keys-{Guid.NewGuid():N}");

    /// <summary>Starts the pinned pgvector image with a random host port and migrates its empty chat database.</summary>
    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            started = true;
            await DockerAsync(timeout.Token, "run", "--detach", "--name", containerName, "--env", "POSTGRES_PASSWORD",
                "--env", "POSTGRES_DB=marmotte_test", "--publish", "127.0.0.1::5432",
                "pgvector/pgvector:0.8.7-pg17-bookworm@sha256:b513c48566e4777cd3370451400000c78eda6a08131d68f4c31d4a76f27bd8fd");
            var port = await DockerAsync(timeout.Token, "port", containerName, "5432/tcp");
            ConnectionString = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = int.Parse(port.Trim().Split(':')[1], CultureInfo.InvariantCulture),
                Database = "marmotte_test",
                Username = "postgres",
                Password = password,
                Timeout = 2,
                Pooling = false
            }.ConnectionString;
            await WaitForDatabaseAsync(timeout.Token);
            await using var provider = Services();
            await provider.GetRequiredService<PostgreSqlMigrations>().ApplyAsync(timeout.Token);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <summary>Creates fresh adapter instances and key-ring readers, modeling process restart without sharing a connection pool.</summary>
    internal ServiceProvider Services(string? connectionString = null)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("Marmotte.ChatUi").PersistKeysToFileSystem(new DirectoryInfo(KeyDirectory));
        services.AddPostgreSqlPersistence(connectionString ?? ConnectionString);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Restarts only the fixture container and waits for its retained database to accept connections.</summary>
    internal async Task RestartAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(1));
        await DockerAsync(deadline.Token, "restart", containerName);
        // Docker may assign a different ephemeral published port when restarting the container.
        var port = await DockerAsync(deadline.Token, "port", containerName, "5432/tcp");
        ConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Port = int.Parse(port.Trim().Split(':')[1], CultureInfo.InvariantCulture)
        }.ConnectionString;
        await WaitForDatabaseAsync(deadline.Token);
    }

    /// <summary>Removes only the random test container, its anonymous volumes, and its key directory, including after failures.</summary>
    public async ValueTask DisposeAsync()
    {
        if (started)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await DockerAsync(timeout.Token, "rm", "--force", "--volumes", containerName);
            started = false;
        }

        if (Directory.Exists(KeyDirectory))
        {
            Directory.Delete(KeyDirectory, recursive: true);
        }
    }

    /// <summary>Waits within the caller's deadline without printing connection strings or server diagnostics.</summary>
    private async Task WaitForDatabaseAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (NpgsqlException)
            {
                await Task.Delay(200, cancellationToken);
            }
        }
    }

    /// <summary>Runs Docker with argument separation and a secret environment value; callers receive only nonsecret command output.</summary>
    private async Task<string> DockerAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["POSTGRES_PASSWORD"] = password;
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Docker test process.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await error;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Docker test command failed.");
            }

            return await output;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
