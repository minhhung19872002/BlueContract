using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueContract.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4BidsContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_appendices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    info_changes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    old_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    new_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    value_change = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_appendices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    procurement_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requires_warehouse_receipt = table.Column<bool>(type: "boolean", nullable: false),
                    bid_decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    duration = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    amount_before_vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    warranty_terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    delivery_terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    guarantee_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    guarantee_bank = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    guarantee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    guarantee_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_return_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    domain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    import_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    default_contract_template_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contract_appendix_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appendix_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    old_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    new_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    old_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    value_change = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_appendix_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_appendix_lines_contract_appendices_appendix_id",
                        column: x => x.appendix_id,
                        principalTable: "contract_appendices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    proposed_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    approved_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    called_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    accepted_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    paid_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_items_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bid_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    signed_date = table.Column<DateOnly>(type: "date", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    package_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    selection_method = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    funding_source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    execution_period = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    attachment_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    attachment_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    attachment_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bid_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bid_decisions_item_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "item_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    his_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    active_ingredient = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    strength = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    specification = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bid_group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bid_decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    winning_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_item_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "item_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bid_decision_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bid_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bid_decision_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_bid_decision_items_bid_decisions_bid_decision_id",
                        column: x => x.bid_decision_id,
                        principalTable: "bid_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bid_decision_suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bid_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    winning_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bid_decision_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "fk_bid_decision_suppliers_bid_decisions_bid_decision_id",
                        column: x => x.bid_decision_id,
                        principalTable: "bid_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bid_decision_items_bid_decision_id_item_id",
                table: "bid_decision_items",
                columns: new[] { "bid_decision_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bid_decision_items_bid_decision_id_supplier_id",
                table: "bid_decision_items",
                columns: new[] { "bid_decision_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bid_decision_suppliers_bid_decision_id_supplier_id",
                table: "bid_decision_suppliers",
                columns: new[] { "bid_decision_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bid_decision_suppliers_supplier_id",
                table: "bid_decision_suppliers",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_bid_decisions_category_id",
                table: "bid_decisions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_bid_decisions_number",
                table: "bid_decisions",
                column: "number",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_bid_decisions_signed_date_status",
                table: "bid_decisions",
                columns: new[] { "signed_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_appendices_contract_id_number",
                table: "contract_appendices",
                columns: new[] { "contract_id", "number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_contract_appendices_document_id",
                table: "contract_appendices",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_appendix_lines_appendix_id",
                table: "contract_appendix_lines",
                column: "appendix_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_items_contract_id_item_id",
                table: "contract_items",
                columns: new[] { "contract_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_items_item_id",
                table: "contract_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_bid_decision_id",
                table: "contracts",
                column: "bid_decision_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_category_id",
                table: "contracts",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_document_id",
                table: "contracts",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_number",
                table: "contracts",
                column: "number",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_status_expiry_date",
                table: "contracts",
                columns: new[] { "status", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_supplier_id_status",
                table: "contracts",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_item_categories_code",
                table: "item_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_bid_decision_id_supplier_id_code",
                table: "items",
                columns: new[] { "bid_decision_id", "supplier_id", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_items_category_id_is_active",
                table: "items",
                columns: new[] { "category_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_items_his_key",
                table: "items",
                column: "his_key");

            migrationBuilder.CreateIndex(
                name: "ix_items_supplier_id",
                table: "items",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bid_decision_items");

            migrationBuilder.DropTable(
                name: "bid_decision_suppliers");

            migrationBuilder.DropTable(
                name: "contract_appendix_lines");

            migrationBuilder.DropTable(
                name: "contract_items");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "bid_decisions");

            migrationBuilder.DropTable(
                name: "contract_appendices");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "item_categories");
        }
    }
}
