using System.Diagnostics.CodeAnalysis;
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Agon.Infrastructure.Migrations
{
    /// <inheritdoc />
    [ExcludeFromCodeCoverage]public partial class AddClarificationRoundCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClarificationRoundCount",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClarificationRoundCount",
                table: "sessions");
        }
    }
}
