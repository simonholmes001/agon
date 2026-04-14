using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentExtractionLifecycleState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extraction_failure_reason",
                table: "session_attachments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extraction_status",
                table: "session_attachments",
                type: "text",
                nullable: false,
                defaultValue: "uploaded");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extraction_failure_reason",
                table: "session_attachments");

            migrationBuilder.DropColumn(
                name: "extraction_status",
                table: "session_attachments");
        }
    }
}
