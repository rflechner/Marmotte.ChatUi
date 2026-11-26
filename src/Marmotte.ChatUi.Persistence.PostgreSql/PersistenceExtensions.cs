using Marmotte.ChatUi.Application.Ports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Composes PostgreSQL adapters without exposing database types to the host or application contracts.</summary>
public static class PersistenceExtensions
{
    /// <summary>Registers a singleton context factory/migrator and scoped repositories; each operation owns its EF context.</summary>
    /// <param name="services">Host service registrations, including a persistent IDataProtectionProvider for credentials.</param>
    /// <param name="connectionString">Server-only PostgreSQL connection string; never log it.</param>
    /// <returns>The supplied collection for composition.</returns>
    /// <remarks>Call PostgreSqlMigrations.ApplyAsync before use. Connections time out at 15s, statements at 30s, lock waits at 10s.</remarks>
    public static IServiceCollection AddPostgreSqlPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContextFactory<ChatDbContext>(options => Database.Configure(options, connectionString));
        services.AddSingleton(provider => new PostgreSqlMigrations(provider.GetRequiredService<IDbContextFactory<ChatDbContext>>()));
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IConnectionProfileRepository, ConnectionProfileRepository>();
        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
        services.AddScoped<IAttachmentMetadataRepository, AttachmentMetadataRepository>();
        services.AddScoped<IModelCredentialStore>(provider => new ModelCredentialStore(
            provider.GetRequiredService<IDbContextFactory<ChatDbContext>>(), provider.GetRequiredService<IDataProtectionProvider>()));
        return services;
    }
}
