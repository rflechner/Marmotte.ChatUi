using Microsoft.EntityFrameworkCore;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Internal EF Core unit of work whose code-first model defines the PostgreSQL schema.</summary>
/// <remarks>Created and disposed per operation; never shared by concurrent calls or retained for a Blazor circuit.</remarks>
internal sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    internal DbSet<ConversationRow> Conversations => Set<ConversationRow>();
    internal DbSet<MessageRow> Messages => Set<MessageRow>();
    internal DbSet<AttachmentRow> Attachments => Set<AttachmentRow>();
    internal DbSet<MessageAttachmentRow> MessageAttachments => Set<MessageAttachmentRow>();
    internal DbSet<ConnectionProfileRow> Profiles => Set<ConnectionProfileRow>();
    internal DbSet<UserPreferencesRow> Preferences => Set<UserPreferencesRow>();
    internal DbSet<ModelCredentialRow> Credentials => Set<ModelCredentialRow>();

    /// <summary>Applies the adapter's explicit relational mappings without annotating domain objects.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("chat");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);
    }
}
