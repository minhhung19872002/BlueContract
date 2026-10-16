using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "liquidated_at",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "acceptance_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    period_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_auto_generated = table.Column<bool>(type: "boolean", nullable: false),
                    conclusion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_return_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valuation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_included_in_valuation = table.Column<bool>(type: "boolean", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceptance_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "liquidation_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    executed_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_return_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidation_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "valuation_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_sequence = table.Column<int>(type: "integer", nullable: false),
                    period_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contract_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    advance_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    previous_cumulative = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    current_period_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    advance_recovered = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    settled_by_liquidation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_request_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payment_request_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_request_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_return_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    liquidation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valuation_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acceptance_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceptance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceptance_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_acceptance_items_acceptance_records_acceptance_id",
                        column: x => x.acceptance_id,
                        principalTable: "acceptance_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "acceptance_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceptance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceptance_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_acceptance_receipts_acceptance_records_acceptance_id",
                        column: x => x.acceptance_id,
                        principalTable: "acceptance_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "liquidation_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valuation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    voucher_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    voucher_date = table.Column<DateOnly>(type: "date", nullable: true),
                    file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    marked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    marked_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidation_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_liquidation_settlements_liquidation_records_liquidation_id",
                        column: x => x.liquidation_id,
                        principalTable: "liquidation_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "liquidation_valuations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valuation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_order_numbers = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidation_valuations", x => x.id);
                    table.ForeignKey(
                        name: "fk_liquidation_valuations_liquidation_records_liquidation_id",
                        column: x => x.liquidation_id,
                        principalTable: "liquidation_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    valuation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    added_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_orders_valuation_records_valuation_id",
                        column: x => x.valuation_id,
                        principalTable: "valuation_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "valuation_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    valuation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceptance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    period_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valuation_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "fk_valuation_acceptances_valuation_records_valuation_id",
                        column: x => x.valuation_id,
                        principalTable: "valuation_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "valuation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    valuation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contract_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    previous_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    current_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valuation_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_valuation_items_valuation_records_valuation_id",
                        column: x => x.valuation_id,
                        principalTable: "valuation_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_items_acceptance_id",
                table: "acceptance_items",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_items_contract_item_id",
                table: "acceptance_items",
                column: "contract_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_receipts_acceptance_id",
                table: "acceptance_receipts",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_receipts_receipt_id",
                table: "acceptance_receipts",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_records_contract_id_status",
                table: "acceptance_records",
                columns: new[] { "contract_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_records_document_id",
                table: "acceptance_records",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_records_number",
                table: "acceptance_records",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_records_supplier_id_status",
                table: "acceptance_records",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_records_valuation_id",
                table: "acceptance_records",
                column: "valuation_id");

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_records_contract_id_status",
                table: "liquidation_records",
                columns: new[] { "contract_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_records_document_id",
                table: "liquidation_records",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_records_number",
                table: "liquidation_records",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_records_supplier_id",
                table: "liquidation_records",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_settlements_liquidation_id",
                table: "liquidation_settlements",
                column: "liquidation_id");

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_valuations_liquidation_id",
                table: "liquidation_valuations",
                column: "liquidation_id");

            migrationBuilder.CreateIndex(
                name: "ix_liquidation_valuations_valuation_id",
                table: "liquidation_valuations",
                column: "valuation_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_valuation_id",
                table: "payment_orders",
                column: "valuation_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_acceptances_acceptance_id",
                table: "valuation_acceptances",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_acceptances_valuation_id",
                table: "valuation_acceptances",
                column: "valuation_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_items_contract_item_id",
                table: "valuation_items",
                column: "contract_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_items_valuation_id",
                table: "valuation_items",
                column: "valuation_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_contract_id_status",
                table: "valuation_records",
                columns: new[] { "contract_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_document_id",
                table: "valuation_records",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_liquidation_id",
                table: "valuation_records",
                column: "liquidation_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_number",
                table: "valuation_records",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_payment_request_document_id",
                table: "valuation_records",
                column: "payment_request_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_payment_request_number",
                table: "valuation_records",
                column: "payment_request_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_valuation_records_supplier_id_payment_status",
                table: "valuation_records",
                columns: new[] { "supplier_id", "payment_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acceptance_items");

            migrationBuilder.DropTable(
                name: "acceptance_receipts");

            migrationBuilder.DropTable(
                name: "liquidation_settlements");

            migrationBuilder.DropTable(
                name: "liquidation_valuations");

            migrationBuilder.DropTable(
                name: "payment_orders");

            migrationBuilder.DropTable(
                name: "valuation_acceptances");

            migrationBuilder.DropTable(
                name: "valuation_items");

            migrationBuilder.DropTable(
                name: "acceptance_records");

            migrationBuilder.DropTable(
                name: "liquidation_records");

            migrationBuilder.DropTable(
                name: "valuation_records");

            migrationBuilder.DropColumn(
                name: "liquidated_at",
                table: "contracts");
        }
    }
}
