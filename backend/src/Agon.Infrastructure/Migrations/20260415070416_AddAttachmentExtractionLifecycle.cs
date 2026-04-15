using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentExtractionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extraction_error",
                table: "session_attachments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "extraction_progress_percent",
                table: "session_attachments",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<string>(
                name: "extraction_status",
                table: "session_attachments",
                type: "text",
                nullable: false,
                defaultValue: "ready");

            migrationBuilder.AddColumn<DateTime>(
                name: "extraction_updated_at",
                table: "session_attachments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extraction_error",
                table: "session_attachments");

            migrationBuilder.DropColumn(
                name: "extraction_progress_percent",
                table: "session_attachments");

            migrationBuilder.DropColumn(
                name: "extraction_status",
                table: "session_attachments");

            migrationBuilder.DropColumn(
                name: "extraction_updated_at",
                table: "session_attachments");
        }
    }
}
