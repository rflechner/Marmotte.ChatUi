using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marmotte.ChatUi.Persistence.PostgreSql.Migrations;

/// <summary>Adds separately protected, owner-bound model credentials to the code-first schema.</summary>
internal partial class ProtectedModelCredentials : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "profile_credentials",
            schema: "chat",
            columns: table => new
            {
                owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                protected_value = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("profile_credentials_pkey", x => new { x.owner_id, x.profile_id });
                table.ForeignKey(
                    name: "profile_credentials_owner_id_profile_id_fkey",
                    columns: x => new { x.owner_id, x.profile_id },
                    principalSchema: "chat",
                    principalTable: "profiles",
                    principalColumns: ["owner_id", "id"],
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "profile_credentials",
            schema: "chat");
    }
}
