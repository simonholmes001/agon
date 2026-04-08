using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialAccessTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "token_usage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false),
                    total_tokens = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_usage_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trial_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    details_json = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trial_controls",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_controls", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "trial_tester_grants",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trial_tester_grants", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_token_usage_records_session_id_occurred_at",
                table: "token_usage_records",
                columns: new[] { "session_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_token_usage_records_user_id_occurred_at",
                table: "token_usage_records",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_token_usage_records_user_id_provider_model_occurred_at",
                table: "token_usage_records",
                columns: new[] { "user_id", "provider", "model", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_trial_audit_events_action_occurred_at",
                table: "trial_audit_events",
                columns: new[] { "action", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_trial_audit_events_occurred_at",
                table: "trial_audit_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_trial_audit_events_user_id_occurred_at",
                table: "trial_audit_events",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_trial_tester_grants_expires_at",
                table: "trial_tester_grants",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_trial_tester_grants_revoked_at",
                table: "trial_tester_grants",
                column: "revoked_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "token_usage_records");

            migrationBuilder.DropTable(
                name: "trial_audit_events");

            migrationBuilder.DropTable(
                name: "trial_controls");

            migrationBuilder.DropTable(
                name: "trial_tester_grants");
        }
    }
}
