using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9IntegrityAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "integrity_check_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    files_checked = table.Column<int>(type: "integer", nullable: false),
                    bytes_checked = table.Column<long>(type: "bigint", nullable: false),
                    mismatch_count = table.Column<int>(type: "integer", nullable: false),
                    missing_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    issues = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrity_check_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_receipt_date_created_at",
                table: "warehouse_receipts",
                columns: new[] { "receipt_date", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_created_at",
                table: "valuation_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_name",
                table: "suppliers",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_proposals_created_at",
                table: "proposals",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_summaries_created_at",
                table: "proposal_summaries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_records_created_at",
                table: "liquidation_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_items_code",
                table: "items",
                column: "code")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_items_name",
                table: "items",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_created_at",
                table: "documents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_documents_title",
                table: "documents",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_created_at",
                table: "contracts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_name",
                table: "contracts",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_number1",
                table: "contracts",
                column: "number")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_call_orders_created_at",
                table: "call_orders",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action",
                table: "audit_logs",
                column: "action")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_records_created_at",
                table: "acceptance_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_integrity_check_runs_started_at",
                table: "integrity_check_runs",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integrity_check_runs");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_receipts_receipt_date_created_at",
                table: "warehouse_receipts");

            migrationBuilder.DropIndex(
                name: "ix_valuation_records_created_at",
                table: "valuation_records");

            migrationBuilder.DropIndex(
                name: "ix_suppliers_name",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "ix_proposals_created_at",
                table: "proposals");

            migrationBuilder.DropIndex(
                name: "ix_proposal_summaries_created_at",
                table: "proposal_summaries");

            migrationBuilder.DropIndex(
                name: "ix_liquidation_records_created_at",
                table: "liquidation_records");

            migrationBuilder.DropIndex(
                name: "ix_items_code",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_name",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_documents_created_at",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_documents_title",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "ix_contracts_created_at",
                table: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_contracts_name",
                table: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_contracts_number1",
                table: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_call_orders_created_at",
                table: "call_orders");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_action",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_acceptance_records_created_at",
                table: "acceptance_records");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
