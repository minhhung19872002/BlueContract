namespace BlueContract.Application.Common;

/// <summary>
/// Business meaning of every unique index (AUDIT-1 §10.6): when two requests race past an "AnyAsync" check, the database
/// rejects the loser and the API answers with the rule that was broken — in Vietnamese — instead of a generic error.
/// A guard test checks that every unique index of the model is listed here.
/// </summary>
public static class UniqueConstraints
{
    private static readonly Dictionary<string, string> Codes = new(StringComparer.Ordinal)
    {
        // "Một … một" business rules.
        ["ux_liquidation_records_contract_active"] = "LIQUIDATION_EXISTS",
        ["ux_workflow_instances_document_open"] = "WORKFLOW_ALREADY_RUNNING",
        ["ux_valuation_records_contract_sequence"] = "VALUATION_SEQUENCE_TAKEN",
        ["ux_contract_appendices_contract_open"] = "APPENDIX_PENDING_EXISTS",
        ["ux_warehouse_receipts_invoice_active"] = "INVOICE_DUPLICATE",
        ["ix_warehouse_receipts_his_receipt_id"] = "HIS_RECEIPT_IN_PROGRESS",
        ["ux_items_free_code"] = "ITEM_CODE_EXISTS",

        // Codes and numbers.
        ["ix_acceptance_records_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ix_call_orders_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ix_liquidation_records_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ix_proposal_summaries_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ix_proposals_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ix_valuation_records_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ix_valuation_records_payment_request_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ux_warehouse_receipts_portal_number"] = "DOCUMENT_NUMBER_TAKEN",
        ["ix_bid_decisions_number"] = "BID_NUMBER_EXISTS",
        ["ix_contracts_number"] = "CONTRACT_NUMBER_EXISTS",
        ["ix_contract_appendices_contract_id_number"] = "APPENDIX_NUMBER_EXISTS",
        ["ix_departments_code"] = "DEPARTMENT_CODE_EXISTS",
        ["ix_facilities_code"] = "FACILITY_CODE_EXISTS",
        ["ix_warehouses_code"] = "WAREHOUSE_CODE_EXISTS",
        ["ix_item_categories_code"] = "ITEM_CATEGORY_CODE_EXISTS",
        ["ix_document_templates_code"] = "TEMPLATE_CODE_EXISTS",
        ["ix_workflow_templates_code"] = "WORKFLOW_CODE_EXISTS",
        ["ix_roles_code"] = "ROLE_CODE_EXISTS",
        ["ix_users_user_name"] = "USER_NAME_EXISTS",
        ["ix_suppliers_tax_code"] = "SUPPLIER_TAX_CODE_EXISTS",
        ["ix_system_settings_key"] = "CONFLICT",
        ["ix_email_templates_event_type"] = "EMAIL_TEMPLATE_CONCURRENT_EDIT",

        // Child rows / technical keys: a concurrent edit of the same parent.
        ["ix_bid_decision_items_bid_decision_id_item_id"] = "BID_ITEM_DUPLICATE",
        ["ix_bid_decision_suppliers_bid_decision_id_supplier_id"] = "BID_SUPPLIER_DUPLICATE",
        ["ix_contract_items_contract_id_item_id"] = "CONTRACT_ITEM_DUPLICATE",
        ["ix_user_data_scopes_user_id_scope_type_value"] = "CONFLICT",
        ["ix_document_versions_document_id_version_number"] = "CONFLICT",
        ["ix_document_template_versions_template_id_version_number"] = "CONFLICT",
        ["ix_warehouse_receipt_lines_receipt_id_line_no"] = "CONFLICT",
        ["ix_workflow_events_instance_id_sequence"] = "CONFLICT",
        ["ix_department_history_entity_id_version"] = "CONFLICT",
        ["ix_supplier_history_entity_id_version"] = "CONFLICT",
        ["ix_user_history_entity_id_version"] = "CONFLICT",
        ["ix_refresh_tokens_token_hash"] = "CONFLICT",
    };

    public static IReadOnlyCollection<string> Names => Codes.Keys;

    /// <summary>Error code for a violated unique index; unknown names fall back to a generic "duplicate" message.</summary>
    public static string CodeFor(string? constraintName) =>
        constraintName is not null && Codes.TryGetValue(constraintName, out var code) ? code : "DUPLICATE_RECORD";
}
