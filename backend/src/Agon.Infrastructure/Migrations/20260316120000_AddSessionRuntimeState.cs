using System.Diagnostics.CodeAnalysis;
﻿using Agon.Infrastructure.Persistence.PostgreSQL;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Agon.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AgonDbContext))]
    [Migration("20260316120000_AddSessionRuntimeState")]
    [ExcludeFromCodeCoverage]
    public partial class AddSessionRuntimeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "clarification_incomplete",
                table: "sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "current_round",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "targeted_loop_count",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tokens_used",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clarification_incomplete",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "current_round",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "targeted_loop_count",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "tokens_used",
                table: "sessions");
        }
    }
}
