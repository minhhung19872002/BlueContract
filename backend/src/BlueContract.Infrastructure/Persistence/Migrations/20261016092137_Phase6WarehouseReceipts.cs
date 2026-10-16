using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6WarehouseReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    system = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    request_body = table.Column<string>(type: "text", nullable: true),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    remote_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    his_receipt_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    his_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
                    warehouse_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bid_decision_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    call_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deliverer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    invoice_symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    invoice_amount_before_vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    invoice_vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    invoice_total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    invoice_seller_tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    invoice_xml_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    invoice_pdf_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    invoice_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invoice_updated_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    validation_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issue_count = table.Column<int>(type: "integer", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_return_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_included_in_acceptance = table.Column<bool>(type: "boolean", nullable: false),
                    acceptance_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_warehouse_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_receipt_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    his_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lot_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouse_receipt_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_warehouse_receipt_lines_warehouse_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "warehouse_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_logs_external_id",
                table: "integration_logs",
                column: "external_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_logs_started_at",
                table: "integration_logs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_integration_logs_status_started_at",
                table: "integration_logs",
                columns: new[] { "status", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipt_lines_contract_item_id",
                table: "warehouse_receipt_lines",
                column: "contract_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipt_lines_receipt_id_line_no",
                table: "warehouse_receipt_lines",
                columns: new[] { "receipt_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_acceptance_id",
                table: "warehouse_receipts",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_call_order_id",
                table: "warehouse_receipts",
                column: "call_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_contract_id_status",
                table: "warehouse_receipts",
                columns: new[] { "contract_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_document_id",
                table: "warehouse_receipts",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_his_receipt_id",
                table: "warehouse_receipts",
                column: "his_receipt_id",
                unique: true,
                filter: "his_receipt_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_number",
                table: "warehouse_receipts",
                column: "number");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_receipt_date",
                table: "warehouse_receipts",
                column: "receipt_date");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_status_validation_status",
                table: "warehouse_receipts",
                columns: new[] { "status", "validation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_supplier_id_status",
                table: "warehouse_receipts",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_receipts_supplier_tax_code_invoice_symbol_invoice",
                table: "warehouse_receipts",
                columns: new[] { "supplier_tax_code", "invoice_symbol", "invoice_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_logs");

            migrationBuilder.DropTable(
                name: "warehouse_receipt_lines");

            migrationBuilder.DropTable(
                name: "warehouse_receipts");
        }
    }
}
