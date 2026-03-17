using Agon.Infrastructure.Persistence.PostgreSQL;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using System.Diagnostics.CodeAnalysis;
#nullable disable
namespace Agon.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(AgonDbContext))]
[Migration("20260316150000_AddSessionAttachments")]
[ExcludeFromCodeCoverage]
    public partial class AddSessionAttachments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "session_attachments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                session_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_name = table.Column<string>(type: "text", nullable: false),
                content_type = table.Column<string>(type: "text", nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                blob_name = table.Column<string>(type: "text", nullable: false),
                blob_uri = table.Column<string>(type: "text", nullable: false),
                access_url = table.Column<string>(type: "text", nullable: false),
                extracted_text = table.Column<string>(type: "text", nullable: true),
                uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_session_attachments", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_session_attachments_session_id",
            table: "session_attachments",
            column: "session_id");

        migrationBuilder.CreateIndex(
            name: "IX_session_attachments_session_id_uploaded_at",
            table: "session_attachments",
            columns: new[] { "session_id", "uploaded_at" });

        migrationBuilder.CreateIndex(
            name: "IX_session_attachments_user_id",
            table: "session_attachments",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "session_attachments");
    }
}
