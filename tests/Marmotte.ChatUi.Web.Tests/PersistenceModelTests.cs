using Marmotte.ChatUi.Persistence.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Checks migration/model agreement and keeps ORM implementation types inside the persistence adapter.</summary>
public sealed class PersistenceModelTests
{
    [Fact]
    public void Code_first_model_has_no_unmigrated_changes_and_exports_no_storage_types()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>();
        Database.Configure(options, "Host=localhost;Database=model_only;Username=design");
        using var context = new ChatDbContext(options.Options);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(2, context.Database.GetMigrations().Count());
        Assert.DoesNotContain(typeof(PostgreSqlMigrations).Assembly.GetExportedTypes(),
            type => typeof(DbContext).IsAssignableFrom(type) || type.Name.EndsWith("Row", StringComparison.Ordinal));
    }
}
