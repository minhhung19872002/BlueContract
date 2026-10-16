using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3WorkflowSigning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "signatures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    signer_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    caption = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    certificate_serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    certificate_subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    certificate_issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hash_before = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash_after = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signatures", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "signing_certificates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_organization = table.Column<bool>(type: "boolean", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    protected_pfx = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signing_certificates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "signing_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prepared_pdf_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    base_pdf_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    data_to_sign = table.Column<string>(type: "text", nullable: true),
                    provider_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    signature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signing_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: false),
                    started_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scope_facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_domain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    condition = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    step_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    target_order = table.Column<int>(type: "integer", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_events_workflow_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assignee_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    role_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allow_edit = table.Column<bool>(type: "boolean", nullable: false),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    return_to = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    signature_anchor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    sla_hours = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    acted_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    on_behalf_of_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    signature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_steps_workflow_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_step_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assignee_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    role_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allow_edit = table.Column<bool>(type: "boolean", nullable: false),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    return_to = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    signature_anchor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    sla_hours = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_step_templates_workflow_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "workflow_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_read_at_created_at",
                table: "notifications",
                columns: new[] { "user_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_signatures_document_id",
                table: "signatures",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_signatures_document_version_id",
                table: "signatures",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_signatures_signer_user_id_signed_at",
                table: "signatures",
                columns: new[] { "signer_user_id", "signed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_signing_certificates_user_id_provider",
                table: "signing_certificates",
                columns: new[] { "user_id", "provider" });

            migrationBuilder.CreateIndex(
                name: "ix_signing_requests_step_instance_id_status",
                table: "signing_requests",
                columns: new[] { "step_instance_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_signing_requests_user_id",
                table: "signing_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_events_instance_id_sequence",
                table: "workflow_events",
                columns: new[] { "instance_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_document_id",
                table: "workflow_instances",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_started_by",
                table: "workflow_instances",
                column: "started_by");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_status_supplier_id",
                table: "workflow_instances",
                columns: new[] { "status", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_templates_template_id_order",
                table: "workflow_step_templates",
                columns: new[] { "template_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_acted_by",
                table: "workflow_steps",
                column: "acted_by");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_instance_id",
                table: "workflow_steps",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_status_assignee_user_id",
                table: "workflow_steps",
                columns: new[] { "status", "assignee_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_status_due_at",
                table: "workflow_steps",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_status_role_code",
                table: "workflow_steps",
                columns: new[] { "status", "role_code" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_templates_code",
                table: "workflow_templates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_templates_document_type_is_active",
                table: "workflow_templates",
                columns: new[] { "document_type", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "signatures");

            migrationBuilder.DropTable(
                name: "signing_certificates");

            migrationBuilder.DropTable(
                name: "signing_requests");

            migrationBuilder.DropTable(
                name: "workflow_events");

            migrationBuilder.DropTable(
                name: "workflow_step_templates");

            migrationBuilder.DropTable(
                name: "workflow_steps");

            migrationBuilder.DropTable(
                name: "workflow_templates");

            migrationBuilder.DropTable(
                name: "workflow_instances");
        }
    }
}
