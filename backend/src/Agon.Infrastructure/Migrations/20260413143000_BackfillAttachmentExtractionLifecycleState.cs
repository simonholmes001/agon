using Agon.Infrastructure.Persistence.PostgreSQL;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agon.Infrastructure.Migrations;

[DbContext(typeof(AgonDbContext))]
[Migration("20260413143000_BackfillAttachmentExtractionLifecycleState")]
public partial class BackfillAttachmentExtractionLifecycleState : Migration
{
    private const string LegacyFailureReason =
        "Text extraction was not available for this legacy attachment. Re-upload the file to analyze it.";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE session_attachments
            SET extraction_status = 'ready',
                extraction_failure_reason = NULL
            WHERE extraction_status = 'uploaded'
              AND COALESCE(BTRIM(extracted_text), '') <> '';
            """);

        migrationBuilder.Sql(
            $"""
            UPDATE session_attachments
            SET extraction_status = 'failed',
                extraction_failure_reason = COALESCE(NULLIF(BTRIM(extraction_failure_reason), ''), '{LegacyFailureReason}')
            WHERE extraction_status = 'uploaded'
              AND COALESCE(BTRIM(extracted_text), '') = '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            UPDATE session_attachments
            SET extraction_status = 'uploaded',
                extraction_failure_reason = NULL
            WHERE extraction_status = 'failed'
              AND extraction_failure_reason = '{LegacyFailureReason}'
              AND COALESCE(BTRIM(extracted_text), '') = '';
            """);
    }
}
