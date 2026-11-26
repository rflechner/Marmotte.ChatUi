using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marmotte.ChatUi.Persistence.PostgreSql;

/// <summary>Maps conversation identity, owner paging, and the application-managed optimistic revision.</summary>
internal sealed class ConversationConfiguration : IEntityTypeConfiguration<ConversationRow>
{
    public void Configure(EntityTypeBuilder<ConversationRow> builder)
    {
        builder.ToTable("conversations", table =>
        {
            table.HasCheckConstraint("conversations_title_check", "length(title) > 0");
            table.HasCheckConstraint("conversations_check", "updated_at >= created_at");
            table.HasCheckConstraint("conversations_revision_check", "revision > 0");
        });
        builder.HasKey(row => row.Id).HasName("conversations_pkey");
        builder.HasAlternateKey(row => new { row.OwnerId, row.Id }).HasName("conversations_owner_id_id_key");
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.OwnerId).HasColumnName("owner_id").HasMaxLength(256);
        builder.Property(row => row.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        builder.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.HasIndex(row => new { row.OwnerId, row.CreatedAt, row.Id }).HasDatabaseName("conversations_owner_page");
    }
}

/// <summary>Maps ordered message pairs, safe lifecycle values, and cascading conversation ownership.</summary>
internal sealed class MessageConfiguration : IEntityTypeConfiguration<MessageRow>
{
    public void Configure(EntityTypeBuilder<MessageRow> builder)
    {
        builder.ToTable("messages", table =>
        {
            table.HasCheckConstraint("messages_sequence_check", "sequence > 0");
            table.HasCheckConstraint("messages_role_check", "role BETWEEN 0 AND 1");
            table.HasCheckConstraint("messages_state_check", "state BETWEEN 0 AND 4");
            table.HasCheckConstraint("messages_failure_check", "failure BETWEEN 0 AND 4");
            table.HasCheckConstraint("messages_check3", "updated_at >= created_at");
            table.HasCheckConstraint("messages_check", "(state = 4) = (failure IS NOT NULL)");
            table.HasCheckConstraint("messages_check1", "role <> 0 OR state = 2");
            table.HasCheckConstraint("messages_check2", "state <> 0 OR content = ''");
        });
        builder.HasKey(row => new { row.OwnerId, row.ConversationId, row.Id }).HasName("messages_pkey");
        builder.HasAlternateKey(row => new { row.OwnerId, row.ConversationId, row.Sequence }).HasName("messages_owner_id_conversation_id_sequence_key");
        builder.HasAlternateKey(row => new { row.OwnerId, row.ConversationId, row.SubmissionId, row.Role }).HasName("messages_owner_id_conversation_id_submission_id_role_key");
        builder.Property(row => row.OwnerId).HasColumnName("owner_id").HasMaxLength(256);
        builder.Property(row => row.ConversationId).HasColumnName("conversation_id");
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.SubmissionId).HasColumnName("submission_id");
        builder.Property(row => row.Sequence).HasColumnName("sequence");
        builder.Property(row => row.Role).HasColumnName("role");
        builder.Property(row => row.Content).HasColumnName("content");
        builder.Property(row => row.State).HasColumnName("state");
        builder.Property(row => row.Failure).HasColumnName("failure");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<ConversationRow>().WithMany().HasForeignKey(row => new { row.OwnerId, row.ConversationId })
            .HasPrincipalKey(row => new { row.OwnerId, row.Id }).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("messages_owner_id_conversation_id_fkey");
        builder.HasIndex(row => new { row.OwnerId, row.ConversationId }).IsUnique().HasFilter("role = 1 AND state IN (0, 1)")
            .HasDatabaseName("messages_one_active_response");
    }
}

/// <summary>Maps immutable attachment metadata and dimensions independently of uploaded bytes.</summary>
internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<AttachmentRow>
{
    public void Configure(EntityTypeBuilder<AttachmentRow> builder)
    {
        builder.ToTable("attachments", table =>
        {
            table.HasCheckConstraint("attachments_size_bytes_check", "size_bytes > 0");
            table.HasCheckConstraint("attachments_state_check", "state BETWEEN 0 AND 3");
            table.HasCheckConstraint("attachments_width_check", "width > 0");
            table.HasCheckConstraint("attachments_height_check", "height > 0");
            table.HasCheckConstraint("attachments_check", "(width IS NULL) = (height IS NULL)");
        });
        builder.HasKey(row => row.Id).HasName("attachments_pkey");
        builder.HasAlternateKey(row => new { row.OwnerId, row.Id }).HasName("attachments_owner_id_id_key");
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.OwnerId).HasColumnName("owner_id").HasMaxLength(256);
        builder.Property(row => row.FileName).HasColumnName("file_name").HasMaxLength(255);
        builder.Property(row => row.MediaType).HasColumnName("media_type").HasMaxLength(127);
        builder.Property(row => row.SizeBytes).HasColumnName("size_bytes");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.State).HasColumnName("state");
        builder.Property(row => row.Width).HasColumnName("width");
        builder.Property(row => row.Height).HasColumnName("height");
    }
}

/// <summary>Maps ordered attachment references with composite foreign keys that prevent cross-owner links.</summary>
internal sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachmentRow>
{
    public void Configure(EntityTypeBuilder<MessageAttachmentRow> builder)
    {
        builder.ToTable("message_attachments", table => table.HasCheckConstraint("message_attachments_position_check", "position >= 0"));
        builder.HasKey(row => new { row.OwnerId, row.ConversationId, row.MessageId, row.AttachmentId }).HasName("message_attachments_pkey");
        builder.HasAlternateKey(row => new { row.OwnerId, row.ConversationId, row.MessageId, row.Position })
            .HasName("message_attachments_owner_id_conversation_id_message_id_pos_key");
        builder.Property(row => row.OwnerId).HasColumnName("owner_id").HasMaxLength(256);
        builder.Property(row => row.ConversationId).HasColumnName("conversation_id");
        builder.Property(row => row.MessageId).HasColumnName("message_id");
        builder.Property(row => row.AttachmentId).HasColumnName("attachment_id");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.HasOne<MessageRow>().WithMany().HasForeignKey(row => new { row.OwnerId, row.ConversationId, row.MessageId })
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("message_attachments_owner_id_conversation_id_message_id_fkey");
        builder.HasOne<AttachmentRow>().WithMany().HasForeignKey(row => new { row.OwnerId, row.AttachmentId })
            .HasPrincipalKey(row => new { row.OwnerId, row.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("message_attachments_owner_id_attachment_id_fkey");
        builder.HasIndex(row => new { row.OwnerId, row.AttachmentId }).HasDatabaseName("message_attachments_reference");
    }
}

/// <summary>Maps validated connection settings without including any model credentials.</summary>
internal sealed class ConnectionProfileConfiguration : IEntityTypeConfiguration<ConnectionProfileRow>
{
    public void Configure(EntityTypeBuilder<ConnectionProfileRow> builder)
    {
        builder.ToTable("profiles", table =>
        {
            table.HasCheckConstraint("profiles_temperature_check", "temperature >= 0 AND temperature < 'Infinity'::double precision");
            table.HasCheckConstraint("profiles_max_output_tokens_check", "max_output_tokens > 0");
            table.HasCheckConstraint("profiles_check", "updated_at >= created_at");
        });
        builder.HasKey(row => row.Id).HasName("profiles_pkey");
        builder.HasAlternateKey(row => new { row.OwnerId, row.Id }).HasName("profiles_owner_id_id_key");
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.OwnerId).HasColumnName("owner_id").HasMaxLength(256);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(100);
        builder.Property(row => row.Endpoint).HasColumnName("endpoint");
        builder.Property(row => row.Model).HasColumnName("model").HasMaxLength(256);
        builder.Property(row => row.Temperature).HasColumnName("temperature");
        builder.Property(row => row.MaxOutputTokens).HasColumnName("max_output_tokens");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(row => new { row.OwnerId, row.CreatedAt, row.Id }).HasDatabaseName("profiles_owner_page");
    }
}

/// <summary>Maps a single theme/profile/model selection per owner with an owned profile constraint.</summary>
internal sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferencesRow>
{
    public void Configure(EntityTypeBuilder<UserPreferencesRow> builder)
    {
        builder.ToTable("preferences", table =>
        {
            table.HasCheckConstraint("preferences_theme_check", "theme BETWEEN 0 AND 2");
            table.HasCheckConstraint("preferences_check", "selected_profile_id IS NOT NULL OR selected_model IS NULL");
        });
        builder.HasKey(row => row.OwnerId).HasName("preferences_pkey");
        builder.Property(row => row.OwnerId).HasColumnName("owner_id").HasMaxLength(256);
        builder.Property(row => row.Theme).HasColumnName("theme");
        builder.Property(row => row.SelectedProfileId).HasColumnName("selected_profile_id");
        builder.Property(row => row.SelectedModel).HasColumnName("selected_model").HasMaxLength(256);
        builder.HasOne<ConnectionProfileRow>().WithMany().HasForeignKey(row => new { row.OwnerId, row.SelectedProfileId })
            .HasPrincipalKey(row => new { row.OwnerId, row.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("preferences_owner_id_selected_profile_id_fkey");
    }
}

/// <summary>Maps protected credential bytes separately from settings and cascades their deletion with the profile.</summary>
internal sealed class ModelCredentialConfiguration : IEntityTypeConfiguration<ModelCredentialRow>
{
    public void Configure(EntityTypeBuilder<ModelCredentialRow> builder)
    {
        builder.ToTable("profile_credentials");
        builder.HasKey(row => new { row.OwnerId, row.ProfileId }).HasName("profile_credentials_pkey");
        builder.Property(row => row.OwnerId).HasColumnName("owner_id").HasMaxLength(256);
        builder.Property(row => row.ProfileId).HasColumnName("profile_id");
        builder.Property(row => row.ProtectedValue).HasColumnName("protected_value");
        builder.HasOne<ConnectionProfileRow>().WithMany().HasForeignKey(row => new { row.OwnerId, row.ProfileId })
            .HasPrincipalKey(row => new { row.OwnerId, row.Id }).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("profile_credentials_owner_id_profile_id_fkey");
    }
}
