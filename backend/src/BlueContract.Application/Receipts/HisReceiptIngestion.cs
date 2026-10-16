using System.Security.Cryptography;
using System.Text.Json;
using BlueContract.Application.Common;
using BlueContract.Application.Integration;
using BlueContract.Application.Proposals;
using BlueContract.Application.Receipts.Validation;
using BlueContract.Domain.Receipts;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Receipts;

/// <summary>
/// Spec §6.4 B2: a receipt from the HIS (push API, pull job or the dev simulator) → map contract / items → validation engine
/// → PDF → 5-signature workflow. Idempotent by HisReceiptId: an identical resend is a no-op, a changed one updates the
/// receipt while nobody has signed it.
/// </summary>
public sealed class HisReceiptIngestion(
    IAppDbContext db,
    DocumentNumbers numbers,
    ReceiptValidationLoader validation,
    ReceiptProcessing processing,
    IAuditLogger audit)
{
    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);

    public static string PayloadHash(HisReceiptPayload payload) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, HashOptions)));

    /// <summary>
    /// Serialized per HIS receipt id (a retry racing the original is answered as a duplicate, not a 500) and per supplier
    /// tax code (two receipts carrying the same invoice are validated one after the other, so only one is accepted).
    /// </summary>
    public Task<Result<HisIngestResultDto>> IngestAsync(HisReceiptPayload p, CancellationToken ct) =>
        db.SerializedAsync([$"his-receipt:{p.HisReceiptId.Trim()}", ReceiptLocks.Invoices(p.SupplierTaxCode)], () => IngestLockedAsync(p, ct), ct);

    private async Task<Result<HisIngestResultDto>> IngestLockedAsync(HisReceiptPayload p, CancellationToken ct)
    {
        var hash = PayloadHash(p);
        var hisId = p.HisReceiptId.Trim();
        var receipt = await db.WarehouseReceipts.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Source == ReceiptSource.His && r.HisReceiptId == hisId, ct);
        HisIngestOutcome outcome;
        var touchedItems = new List<Guid>();
        var touchedOrders = new List<Guid?>();

        if (receipt is not null)
        {
            if (receipt.HisPayloadHash == hash)
            {
                return ToResult(receipt, HisIngestOutcome.Duplicate);
            }

            if (receipt.Status is ReceiptStatus.Completed or ReceiptStatus.Cancelled || await processing.HasSignaturesAsync(receipt, ct))
            {
                return Error.Conflict("RECEIPT_LOCKED_SIGNED");
            }

            if (receipt.Type != p.Type)
            {
                return Error.Validation("RECEIPT_TYPE_LOCKED");
            }

            touchedItems.AddRange(receipt.Lines.Where(l => l.ContractItemId is not null).Select(l => l.ContractItemId!.Value));
            touchedOrders.Add(receipt.CallOrderId);
            outcome = HisIngestOutcome.Updated;
        }
        else
        {
            receipt = new WarehouseReceipt(
                string.IsNullOrWhiteSpace(p.ReceiptNumber) ? await numbers.NextAsync(p.Type == ReceiptType.Return ? DocumentNumbers.Return : DocumentNumbers.Receipt, ct) : p.ReceiptNumber,
                p.Type, ReceiptSource.His, hisId, p.ReceiptDate, p.WarehouseCode, null, p.SupplierTaxCode, p.BidDecisionNumber);
            db.WarehouseReceipts.Add(receipt);
            outcome = HisIngestOutcome.Created;
        }

        var warehouseId = await db.Warehouses.Where(w => w.HisWarehouseCode == p.WarehouseCode.Trim() || w.Code == p.WarehouseCode.Trim())
            .Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct);
        receipt.UpdateHeader(string.IsNullOrWhiteSpace(p.ReceiptNumber) ? receipt.Number : p.ReceiptNumber, p.ReceiptDate, p.WarehouseCode, warehouseId,
            p.SupplierTaxCode, p.BidDecisionNumber, p.DelivererName, p.Note);
        receipt.SetLines([.. p.Lines.Select(l => new ReceiptLineDraft(l.HisKey, l.ItemCode, l.ItemName, l.Unit ?? string.Empty, l.LotNumber, l.ExpiryDate,
            l.Quantity, l.UnitPrice, l.VatRate ?? 0))]);
        if (p.Invoice is { } inv)
        {
            receipt.SetInvoice(new InvoiceInfo(inv.Symbol, inv.Number, inv.Date, inv.AmountBeforeVat, inv.VatAmount, inv.TotalAmount, inv.SellerTaxCode), "HIS", processing.Now);
        }

        receipt.SetHisPayloadHash(hash);
        await validation.MapAsync(receipt, p.ContractNumber, p.CallOrderNumber, ct);
        await validation.ValidateAsync(receipt, ct);

        var generated = await processing.GenerateDocumentAsync(receipt, outcome == HisIngestOutcome.Created ? "Nhận phiếu từ HIS" : "HIS gửi lại phiếu đã sửa", ct);
        if (!generated.IsSuccess)
        {
            return generated.Error!;
        }

        audit.Log(outcome == HisIngestOutcome.Created ? "HisReceiptReceived" : "HisReceiptUpdated", nameof(WarehouseReceipt), receipt.Id.ToString(),
            new { receipt.Number, receipt.HisReceiptId, receipt.ValidationStatus, issues = receipt.Issues.Count });
        await db.SaveChangesAsync(ct);

        if (receipt.DocumentId is not null)
        {
            var (userId, userName) = await processing.HisServiceUserAsync(ct);
            var started = await processing.StartSigningAsync(receipt, userId, userName, ct);
            if (!started.IsSuccess)
            {
                return started.Error!;
            }

            await db.SaveChangesAsync(ct);
        }

        touchedItems.AddRange(receipt.Lines.Where(l => l.ContractItemId is not null).Select(l => l.ContractItemId!.Value));
        touchedOrders.Add(receipt.CallOrderId);
        await processing.RecalculateAsync(touchedItems, touchedOrders, ct);
        return ToResult(receipt, outcome);
    }

    private static HisIngestResultDto ToResult(WarehouseReceipt r, HisIngestOutcome outcome) =>
        new(r.Id, r.Number, outcome, r.Status, r.ValidationStatus, r.ContractNumber,
            [.. r.Issues.Select(i => new HisIngestIssueDto(i.Code, i.Severity, i.Message, i.LineNo))]);
}
