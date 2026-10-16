namespace BlueContract.Application.Receipts;

/// <summary>
/// Lock keys for <c>IAppDbContext.SerializedAsync</c>. The duplicate-invoice rule compares a receipt with the other receipts
/// of the same seller, so everything that can change which receipt holds an invoice is serialized per supplier tax code;
/// the unique index <c>ux_warehouse_receipts_invoice_active</c> is the database backstop (AUDIT-1 §10.6).
/// </summary>
public static class ReceiptLocks
{
    public static string Invoices(string? supplierTaxCode) => $"receipt-invoices:{supplierTaxCode?.Trim() ?? "-"}";
}
