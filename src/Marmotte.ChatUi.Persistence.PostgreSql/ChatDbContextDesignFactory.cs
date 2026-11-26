using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Lets dotnet-ef construct the internal code-first context without starting Web, OIDC, or Aspire.</summary>
/// <remarks>Reads ConnectionStrings__chatdb from the environment; scaffolding and script generation do not connect.</remarks>
internal sealed class ChatDbContextDesignFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    /// <summary>Creates a caller-owned context with runtime-equivalent mappings; missing configuration fails explicitly.</summary>
    public ChatDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__chatdb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Set ConnectionStrings__chatdb for EF tooling. Scaffolding and script generation accept a placeholder connection string without connecting.");
        }

        var options = new DbContextOptionsBuilder<ChatDbContext>();
        Database.Configure(options, connectionString);
        return new ChatDbContext(options.Options);
    }
}
