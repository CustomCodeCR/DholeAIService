using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.AI.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260730160100_AddEmailAnalysisJobs")]
public sealed class AddEmailAnalysisJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiEmailAnalysisJobs",
            schema: "ai",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                external_request_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                email_extraction_job_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                email_message_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                email_attachment_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true
                ),
                payload_url = table.Column<string>(
                    type: "character varying(2000)",
                    maxLength: 2000,
                    nullable: false
                ),
                request_hash = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false
                ),
                correlation_id = table.Column<string>(
                    type: "character varying(150)",
                    maxLength: 150,
                    nullable: false
                ),
                status = table.Column<string>(
                    type: "character varying(50)",
                    maxLength: 50,
                    nullable: false
                ),
                attempt_count = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 0
                ),
                max_attempt_count = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 3
                ),
                next_attempt_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                lease_owner = table.Column<string>(
                    type: "character varying(250)",
                    maxLength: 250,
                    nullable: true
                ),
                lease_expires_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                last_heartbeat_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                ai_execution_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true
                ),
                result_json = table.Column<string>(
                    type: "jsonb",
                    nullable: true
                ),
                error_code = table.Column<string>(
                    type: "character varying(250)",
                    maxLength: 250,
                    nullable: true
                ),
                error_message = table.Column<string>(
                    type: "character varying(4000)",
                    maxLength: 4000,
                    nullable: true
                ),
                started_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                completed_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                version = table.Column<int>(
                    type: "integer",
                    nullable: false,
                    defaultValue: 1
                ),
                created_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                created_by = table.Column<string>(
                    type: "text",
                    nullable: true
                ),
                updated_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                updated_by = table.Column<string>(
                    type: "text",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("p_k_ai_email_analysis_jobs", x => x.id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "i_x_ai_email_analysis_jobs_external_request_id",
            schema: "ai",
            table: "AiEmailAnalysisJobs",
            column: "external_request_id",
            unique: true
        );
        migrationBuilder.CreateIndex(
            name: "i_x_ai_email_analysis_jobs_email_extraction_job_id",
            schema: "ai",
            table: "AiEmailAnalysisJobs",
            column: "email_extraction_job_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_ai_email_analysis_jobs_email_message_id",
            schema: "ai",
            table: "AiEmailAnalysisJobs",
            column: "email_message_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_ai_email_analysis_jobs_request_hash",
            schema: "ai",
            table: "AiEmailAnalysisJobs",
            column: "request_hash"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_ai_email_analysis_jobs_ai_execution_id",
            schema: "ai",
            table: "AiEmailAnalysisJobs",
            column: "ai_execution_id"
        );
        migrationBuilder.CreateIndex(
            name: "ix_ai_email_jobs_queue",
            schema: "ai",
            table: "AiEmailAnalysisJobs",
            columns: ["status", "next_attempt_at_utc", "created_at_utc"]
        );
        migrationBuilder.CreateIndex(
            name: "ix_ai_email_jobs_lease",
            schema: "ai",
            table: "AiEmailAnalysisJobs",
            columns: ["status", "lease_expires_at_utc"]
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiEmailAnalysisJobs",
            schema: "ai"
        );
    }
}
