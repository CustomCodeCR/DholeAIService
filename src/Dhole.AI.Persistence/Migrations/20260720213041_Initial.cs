using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.AI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "AiConnections",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    provider_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    base_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    secret_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_health_check_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_health_error = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AiPromptTemplates",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    system_prompt = table.Column<string>(type: "text", nullable: true),
                    user_prompt_template = table.Column<string>(type: "text", nullable: true),
                    variables_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_prompt_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consumer_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    headers_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AiModels",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_model_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    capabilities = table.Column<int>(type: "integer", nullable: false),
                    context_window = table.Column<int>(type: "integer", nullable: true),
                    maximum_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    input_cost_per_million_tokens = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    output_cost_per_million_tokens = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    is_local = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_availability_check_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_availability_error = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_models", x => x.id);
                    table.ForeignKey(
                        name: "FK_AiModels_AiConnections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "ai",
                        principalTable: "AiConnections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiProfiles",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    prompt_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    routing_mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    response_format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    temperature = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    maximum_output_tokens = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    json_schema = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_profiles", x => x.id);
                    table.ForeignKey(
                        name: "FK_AiProfiles_AiPromptTemplates_prompt_template_id",
                        column: x => x.prompt_template_id,
                        principalSchema: "ai",
                        principalTable: "AiPromptTemplates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiExecutions",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    prompt_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    execution_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    request_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    output_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    selected_connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    selected_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    output_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, defaultValue: 0m),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    finish_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    error_code = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_executions", x => x.id);
                    table.ForeignKey(
                        name: "FK_AiExecutions_AiConnections_selected_connection_id",
                        column: x => x.selected_connection_id,
                        principalSchema: "ai",
                        principalTable: "AiConnections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiExecutions_AiModels_selected_model_id",
                        column: x => x.selected_model_id,
                        principalSchema: "ai",
                        principalTable: "AiModels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiExecutions_AiProfiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "ai",
                        principalTable: "AiProfiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiExecutions_AiPromptTemplates_prompt_template_id",
                        column: x => x.prompt_template_id,
                        principalSchema: "ai",
                        principalTable: "AiPromptTemplates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiProfileModels",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_fallback = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_profile_models", x => x.id);
                    table.ForeignKey(
                        name: "FK_AiProfileModels_AiModels_model_id",
                        column: x => x.model_id,
                        principalSchema: "ai",
                        principalTable: "AiModels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_ai_profile_models_ai_profiles_ai_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "ai",
                        principalTable: "AiProfiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiExecutionAttempts",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_model_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    output_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, defaultValue: 0m),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    finish_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    error_code = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_ai_execution_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_AiExecutionAttempts_AiConnections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "ai",
                        principalTable: "AiConnections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiExecutionAttempts_AiModels_model_id",
                        column: x => x.model_id,
                        principalSchema: "ai",
                        principalTable: "AiModels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_ai_execution_attempts_ai_executions_ai_execution_id",
                        column: x => x.execution_id,
                        principalSchema: "ai",
                        principalTable: "AiExecutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_is_active",
                schema: "ai",
                table: "AiConnections",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_last_health_check_at_utc",
                schema: "ai",
                table: "AiConnections",
                column: "last_health_check_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_provider_type",
                schema: "ai",
                table: "AiConnections",
                column: "provider_type");

            migrationBuilder.CreateIndex(
                name: "IX_AiConnections_status",
                schema: "ai",
                table: "AiConnections",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_ai_connections_name",
                schema: "ai",
                table: "AiConnections",
                column: "name",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_connection_id",
                schema: "ai",
                table: "AiExecutionAttempts",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_execution_id",
                schema: "ai",
                table: "AiExecutionAttempts",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_model_id",
                schema: "ai",
                table: "AiExecutionAttempts",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_provider_type",
                schema: "ai",
                table: "AiExecutionAttempts",
                column: "provider_type");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_started_at_utc",
                schema: "ai",
                table: "AiExecutionAttempts",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_status",
                schema: "ai",
                table: "AiExecutionAttempts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_ai_execution_attempts_execution_number",
                schema: "ai",
                table: "AiExecutionAttempts",
                columns: new[] { "execution_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_executions_profile_status_started",
                schema: "ai",
                table: "AiExecutions",
                columns: new[] { "profile_key", "status", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_completed_at_utc",
                schema: "ai",
                table: "AiExecutions",
                column: "completed_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_correlation_id",
                schema: "ai",
                table: "AiExecutions",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_execution_type",
                schema: "ai",
                table: "AiExecutions",
                column: "execution_type");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_profile_id",
                schema: "ai",
                table: "AiExecutions",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_profile_key",
                schema: "ai",
                table: "AiExecutions",
                column: "profile_key");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_prompt_template_id",
                schema: "ai",
                table: "AiExecutions",
                column: "prompt_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_request_hash",
                schema: "ai",
                table: "AiExecutions",
                column: "request_hash");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_selected_connection_id",
                schema: "ai",
                table: "AiExecutions",
                column: "selected_connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_selected_model_id",
                schema: "ai",
                table: "AiExecutions",
                column: "selected_model_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_started_at_utc",
                schema: "ai",
                table: "AiExecutions",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_status",
                schema: "ai",
                table: "AiExecutions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_capabilities",
                schema: "ai",
                table: "AiModels",
                column: "capabilities");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_connection_id",
                schema: "ai",
                table: "AiModels",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_external_model_id",
                schema: "ai",
                table: "AiModels",
                column: "external_model_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_is_active",
                schema: "ai",
                table: "AiModels",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_is_local",
                schema: "ai",
                table: "AiModels",
                column: "is_local");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_name",
                schema: "ai",
                table: "AiModels",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_status",
                schema: "ai",
                table: "AiModels",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_ai_models_connection_external_model",
                schema: "ai",
                table: "AiModels",
                columns: new[] { "connection_id", "external_model_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfileModels_is_fallback",
                schema: "ai",
                table: "AiProfileModels",
                column: "is_fallback");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfileModels_model_id",
                schema: "ai",
                table: "AiProfileModels",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfileModels_priority",
                schema: "ai",
                table: "AiProfileModels",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfileModels_profile_id",
                schema: "ai",
                table: "AiProfileModels",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ux_ai_profile_models_profile_model",
                schema: "ai",
                table: "AiProfileModels",
                columns: new[] { "profile_id", "model_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ai_profile_models_profile_priority",
                schema: "ai",
                table: "AiProfileModels",
                columns: new[] { "profile_id", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProfiles_is_active",
                schema: "ai",
                table: "AiProfiles",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfiles_name",
                schema: "ai",
                table: "AiProfiles",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfiles_prompt_template_id",
                schema: "ai",
                table: "AiProfiles",
                column: "prompt_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfiles_response_format",
                schema: "ai",
                table: "AiProfiles",
                column: "response_format");

            migrationBuilder.CreateIndex(
                name: "IX_AiProfiles_routing_mode",
                schema: "ai",
                table: "AiProfiles",
                column: "routing_mode");

            migrationBuilder.CreateIndex(
                name: "ux_ai_profiles_key",
                schema: "ai",
                table: "AiProfiles",
                column: "key",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_AiPromptTemplates_is_active",
                schema: "ai",
                table: "AiPromptTemplates",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_AiPromptTemplates_name",
                schema: "ai",
                table: "AiPromptTemplates",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ux_ai_prompt_templates_key",
                schema: "ai",
                table: "AiPromptTemplates",
                column: "key",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_event_id_consumer_service",
                schema: "ai",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_service" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_status_created_at",
                schema: "ai",
                table: "inbox_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                schema: "ai",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_status_created_at",
                schema: "ai",
                table: "outbox_messages",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiExecutionAttempts",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "AiProfileModels",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "AiExecutions",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "AiModels",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "AiProfiles",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "AiConnections",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "AiPromptTemplates",
                schema: "ai");
        }
    }
}
