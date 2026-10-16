using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10UniqueRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "used_request_signatures",
                columns: table => new
                {
                    signature = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_used_request_signatures", x => x.signature);
                });

            migrationBuilder.CreateIndex(
                name: "ux_workflow_instances_document_open",
                table: "workflow_instances",
                column: "document_id",
                unique: true,
                filter: "status IN ('Running', 'Returned')");

            migrationBuilder.CreateIndex(
                name: "ux_warehouse_receipts_invoice_active",
                table: "warehouse_receipts",
                columns: new[] { "supplier_tax_code", "invoice_symbol", "invoice_number" },
                unique: true,
                filter: "invoice_number IS NOT NULL AND status <> 'Cancelled' AND validation_status <> 'Invalid'");

            migrationBuilder.CreateIndex(
                name: "ux_warehouse_receipts_portal_number",
                table: "warehouse_receipts",
                column: "number",
                unique: true,
                filter: "source <> 'His'");

            migrationBuilder.CreateIndex(
                name: "ux_valuation_records_contract_sequence",
                table: "valuation_records",
                columns: new[] { "contract_id", "payment_sequence" },
                unique: true,
                filter: "status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ux_liquidation_records_contract_active",
                table: "liquidation_records",
                column: "contract_id",
                unique: true,
                filter: "status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ux_items_free_code",
                table: "items",
                columns: new[] { "code", "supplier_id" },
                unique: true,
                filter: "bid_decision_id IS NULL AND is_deleted = false")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_contract_appendices_contract_open",
                table: "contract_appendices",
                column: "contract_id",
                unique: true,
                filter: "status IN ('Draft', 'InWorkflow') AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_used_request_signatures_received_at",
                table: "used_request_signatures",
                column: "received_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "used_request_signatures");

            migrationBuilder.DropIndex(
                name: "ux_workflow_instances_document_open",
                table: "workflow_instances");

            migrationBuilder.DropIndex(
                name: "ux_warehouse_receipts_invoice_active",
                table: "warehouse_receipts");

            migrationBuilder.DropIndex(
                name: "ux_warehouse_receipts_portal_number",
                table: "warehouse_receipts");

            migrationBuilder.DropIndex(
                name: "ux_valuation_records_contract_sequence",
                table: "valuation_records");

            migrationBuilder.DropIndex(
                name: "ux_liquidation_records_contract_active",
                table: "liquidation_records");

            migrationBuilder.DropIndex(
                name: "ux_items_free_code",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ux_contract_appendices_contract_open",
                table: "contract_appendices");
        }
    }
}
