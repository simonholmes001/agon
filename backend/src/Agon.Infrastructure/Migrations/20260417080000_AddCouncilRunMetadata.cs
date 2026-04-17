using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCouncilRunMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "council_run_completed_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "council_run_failed_reason",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "council_run_first_progress_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "council_run_last_progress_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "council_run_phase",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "council_run_started_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "council_run_completed_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "council_run_failed_reason",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "council_run_first_progress_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "council_run_last_progress_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "council_run_phase",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "council_run_started_at",
                table: "sessions");
        }
    }
}
