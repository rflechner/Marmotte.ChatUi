using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marmotte.ChatUi.Persistence.PostgreSql.Migrations;

/// <summary>Creates the initial code-first history, attachment metadata, profile, and preference schema.</summary>
internal partial class InitialHistoryAndSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "chat");

        migrationBuilder.CreateTable(
            name: "attachments",
            schema: "chat",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                media_type = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                width = table.Column<int>(type: "integer", nullable: true),
                height = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("attachments_pkey", x => x.id);
                table.UniqueConstraint("attachments_owner_id_id_key", x => new { x.owner_id, x.id });
                table.CheckConstraint("attachments_check", "(width IS NULL) = (height IS NULL)");
                table.CheckConstraint("attachments_height_check", "height > 0");
                table.CheckConstraint("attachments_size_bytes_check", "size_bytes > 0");
                table.CheckConstraint("attachments_state_check", "state BETWEEN 0 AND 3");
                table.CheckConstraint("attachments_width_check", "width > 0");
            });

        migrationBuilder.CreateTable(
            name: "conversations",
            schema: "chat",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("conversations_pkey", x => x.id);
                table.UniqueConstraint("conversations_owner_id_id_key", x => new { x.owner_id, x.id });
                table.CheckConstraint("conversations_revision_check", "revision > 0");
                table.CheckConstraint("conversations_title_check", "length(title) > 0");
                table.CheckConstraint("conversations_check", "updated_at >= created_at");
            });

        migrationBuilder.CreateTable(
            name: "profiles",
            schema: "chat",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                endpoint = table.Column<string>(type: "text", nullable: false),
                model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                temperature = table.Column<double>(type: "double precision", nullable: true),
                max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("profiles_pkey", x => x.id);
                table.UniqueConstraint("profiles_owner_id_id_key", x => new { x.owner_id, x.id });
                table.CheckConstraint("profiles_max_output_tokens_check", "max_output_tokens > 0");
                table.CheckConstraint("profiles_temperature_check", "temperature >= 0 AND temperature < 'Infinity'::double precision");
                table.CheckConstraint("profiles_check", "updated_at >= created_at");
            });

        migrationBuilder.CreateTable(
            name: "messages",
            schema: "chat",
            columns: table => new
            {
                owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                id = table.Column<Guid>(type: "uuid", nullable: false),
                submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false),
                role = table.Column<int>(type: "integer", nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                failure = table.Column<int>(type: "integer", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("messages_pkey", x => new { x.owner_id, x.conversation_id, x.id });
                table.UniqueConstraint("messages_owner_id_conversation_id_sequence_key", x => new { x.owner_id, x.conversation_id, x.sequence });
                table.UniqueConstraint("messages_owner_id_conversation_id_submission_id_role_key", x => new { x.owner_id, x.conversation_id, x.submission_id, x.role });
                table.CheckConstraint("messages_check", "(state = 4) = (failure IS NOT NULL)");
                table.CheckConstraint("messages_check1", "role <> 0 OR state = 2");
                table.CheckConstraint("messages_check2", "state <> 0 OR content = ''");
                table.CheckConstraint("messages_failure_check", "failure BETWEEN 0 AND 4");
                table.CheckConstraint("messages_role_check", "role BETWEEN 0 AND 1");
                table.CheckConstraint("messages_sequence_check", "sequence > 0");
                table.CheckConstraint("messages_state_check", "state BETWEEN 0 AND 4");
                table.CheckConstraint("messages_check3", "updated_at >= created_at");
                table.ForeignKey(
                    name: "messages_owner_id_conversation_id_fkey",
                    columns: x => new { x.owner_id, x.conversation_id },
                    principalSchema: "chat",
                    principalTable: "conversations",
                    principalColumns: ["owner_id", "id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "preferences",
            schema: "chat",
            columns: table => new
            {
                owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                theme = table.Column<int>(type: "integer", nullable: false),
                selected_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                selected_model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("preferences_pkey", x => x.owner_id);
                table.CheckConstraint("preferences_check", "selected_profile_id IS NOT NULL OR selected_model IS NULL");
                table.CheckConstraint("preferences_theme_check", "theme BETWEEN 0 AND 2");
                table.ForeignKey(
                    name: "preferences_owner_id_selected_profile_id_fkey",
                    columns: x => new { x.owner_id, x.selected_profile_id },
                    principalSchema: "chat",
                    principalTable: "profiles",
                    principalColumns: ["owner_id", "id"]);
            });

        migrationBuilder.CreateTable(
            name: "message_attachments",
            schema: "chat",
            columns: table => new
            {
                owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("message_attachments_pkey", x => new { x.owner_id, x.conversation_id, x.message_id, x.attachment_id });
                table.UniqueConstraint("message_attachments_owner_id_conversation_id_message_id_pos_key", x => new { x.owner_id, x.conversation_id, x.message_id, x.position });
                table.CheckConstraint("message_attachments_position_check", "position >= 0");
                table.ForeignKey(
                    name: "message_attachments_owner_id_attachment_id_fkey",
                    columns: x => new { x.owner_id, x.attachment_id },
                    principalSchema: "chat",
                    principalTable: "attachments",
                    principalColumns: ["owner_id", "id"]);
                table.ForeignKey(
                    name: "message_attachments_owner_id_conversation_id_message_id_fkey",
                    columns: x => new { x.owner_id, x.conversation_id, x.message_id },
                    principalSchema: "chat",
                    principalTable: "messages",
                    principalColumns: ["owner_id", "conversation_id", "id"],
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "conversations_owner_page",
            schema: "chat",
            table: "conversations",
            columns: ["owner_id", "created_at", "id"]);

        migrationBuilder.CreateIndex(
            name: "message_attachments_reference",
            schema: "chat",
            table: "message_attachments",
            columns: ["owner_id", "attachment_id"]);

        migrationBuilder.CreateIndex(
            name: "messages_one_active_response",
            schema: "chat",
            table: "messages",
            columns: ["owner_id", "conversation_id"],
            unique: true,
            filter: "role = 1 AND state IN (0, 1)");

        migrationBuilder.CreateIndex(
            name: "IX_preferences_owner_id_selected_profile_id",
            schema: "chat",
            table: "preferences",
            columns: ["owner_id", "selected_profile_id"]);

        migrationBuilder.CreateIndex(
            name: "profiles_owner_page",
            schema: "chat",
            table: "profiles",
            columns: ["owner_id", "created_at", "id"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "message_attachments",
            schema: "chat");

        migrationBuilder.DropTable(
            name: "preferences",
            schema: "chat");

        migrationBuilder.DropTable(
            name: "attachments",
            schema: "chat");

        migrationBuilder.DropTable(
            name: "messages",
            schema: "chat");

        migrationBuilder.DropTable(
            name: "profiles",
            schema: "chat");

        migrationBuilder.DropTable(
            name: "conversations",
            schema: "chat");
    }
}
