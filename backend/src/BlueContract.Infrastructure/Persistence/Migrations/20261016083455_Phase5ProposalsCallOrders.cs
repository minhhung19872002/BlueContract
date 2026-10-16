using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ProposalsCallOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "call_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_date = table.Column<DateOnly>(type: "date", nullable: false),
                    delivery_deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    supplier_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_call_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => new { x.prefix, x.year });
                });

            migrationBuilder.CreateTable(
                name: "proposal_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    domain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregator_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposal_summaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    period_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "call_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contract_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    confirmed_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    shortage_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    delivered_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_call_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_call_order_lines_call_orders_call_order_id",
                        column: x => x.call_order_id,
                        principalTable: "call_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_summary_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contract_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    proposed_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    opening_stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    received = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    issued = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    current_stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    contract_remaining = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    approved_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    called_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposal_summary_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposal_summary_lines_proposal_summaries_summary_id",
                        column: x => x.summary_id,
                        principalTable: "proposal_summaries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_summary_sources",
                columns: table => new
                {
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposal_summary_sources", x => new { x.summary_id, x.proposal_id });
                    table.ForeignKey(
                        name: "fk_proposal_summary_sources_proposal_summaries_summary_id",
                        column: x => x.summary_id,
                        principalTable: "proposal_summaries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contract_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    his_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    opening_stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    received = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    issued = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    current_stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    contract_remaining = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    proposed_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposal_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposal_lines_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_call_order_lines_call_order_id",
                table: "call_order_lines",
                column: "call_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_call_order_lines_contract_item_id",
                table: "call_order_lines",
                column: "contract_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_call_order_lines_summary_line_id",
                table: "call_order_lines",
                column: "summary_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_call_orders_number",
                table: "call_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_call_orders_status_delivery_deadline",
                table: "call_orders",
                columns: new[] { "status", "delivery_deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_call_orders_summary_id",
                table: "call_orders",
                column: "summary_id");

            migrationBuilder.CreateIndex(
                name: "ix_call_orders_supplier_id_status",
                table: "call_orders",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_proposal_lines_contract_item_id",
                table: "proposal_lines",
                column: "contract_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_lines_proposal_id",
                table: "proposal_lines",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_summaries_document_id",
                table: "proposal_summaries",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_summaries_domain_status",
                table: "proposal_summaries",
                columns: new[] { "domain", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_proposal_summaries_number",
                table: "proposal_summaries",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proposal_summary_lines_contract_item_id",
                table: "proposal_summary_lines",
                column: "contract_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_summary_lines_summary_id",
                table: "proposal_summary_lines",
                column: "summary_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_summary_sources_proposal_id",
                table: "proposal_summary_sources",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_department_id_status",
                table: "proposals",
                columns: new[] { "department_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_proposals_document_id",
                table: "proposals",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_domain_status",
                table: "proposals",
                columns: new[] { "domain", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_proposals_number",
                table: "proposals",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proposals_summary_id",
                table: "proposals",
                column: "summary_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "call_order_lines");

            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropTable(
                name: "proposal_lines");

            migrationBuilder.DropTable(
                name: "proposal_summary_lines");

            migrationBuilder.DropTable(
                name: "proposal_summary_sources");

            migrationBuilder.DropTable(
                name: "call_orders");

            migrationBuilder.DropTable(
                name: "proposals");

            migrationBuilder.DropTable(
                name: "proposal_summaries");
        }
    }
}
