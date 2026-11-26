using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Persistence.PostgreSql;
using Microsoft.AspNetCore.DataProtection;

namespace Marmotte.ChatUi.Web;

/// <summary>Composes application use cases, PostgreSQL, persistent credential keys, and startup migrations in the host.</summary>
internal static class PersistenceHostingExtensions
{
    /// <summary>Enables persistence when chatdb is configured; an unconfigured standalone host keeps its landing-page mode.</summary>
    /// <remarks>Keys live outside PostgreSQL. The directory requires private filesystem permissions and independent backups.</remarks>
    internal static void AddHostPersistence(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("chatdb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var keyDirectory = builder.Configuration["Persistence:KeyDirectory"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marmotte.ChatUi", "keys");
        if (!Path.IsPathFullyQualified(keyDirectory))
        {
            throw new InvalidOperationException("Persistence:KeyDirectory must be an absolute path.");
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(keyDirectory);
        }
        else
        {
            Directory.CreateDirectory(keyDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        builder.Services.AddDataProtection()
            .SetApplicationName("Marmotte.ChatUi")
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        builder.Services.AddPostgreSqlPersistence(connectionString);
        builder.Services.AddScoped<ConversationService>();
        builder.Services.AddScoped<IConversationService>(services => services.GetRequiredService<ConversationService>());
        builder.Services.AddScoped<IConnectionProfileService, ConnectionProfileService>();
        builder.Services.AddScoped<IUserPreferencesService, UserPreferencesService>();
        builder.Services.AddScoped<ModelCredentialService>();
        builder.Services.AddHostedService<PersistenceStartup>();
    }
}

/// <summary>Completes migrations before Web accepts requests; failure prevents startup and Aspire readiness.</summary>
internal sealed class PersistenceStartup(PostgreSqlMigrations migrations) : IHostedService
{
    /// <summary>Applies the schema within two minutes, respecting host shutdown and per-statement database deadlines.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        await migrations.ApplyAsync(deadline.Token).ConfigureAwait(false);
    }

    /// <summary>Needs no shutdown work; each migration and repository operation disposes its own context.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
