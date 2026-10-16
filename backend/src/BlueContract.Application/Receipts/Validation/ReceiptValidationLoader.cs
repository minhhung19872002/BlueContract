using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Receipts.Validation;

/// <summary>
/// Maps a receipt onto the portal's data (spec §6.4 B2: NCC by MST, hợp đồng by Số QĐ + MST, mặt hàng by HIS key / item
/// code, đơn gọi hàng) and runs the validation engine with the quantities of the other receipts.
/// </summary>
public sealed class ReceiptValidationLoader(IAppDbContext db, ReceiptValidator validator, SettingService settings, IClock clock)
{
    /// <summary>Received quantities count receipts the warehouse handled that are not Invalid (see ADR-040).</summary>
    public static IQueryable<WarehouseReceipt> Counted(IQueryable<WarehouseReceipt> receipts) =>
        receipts.Where(r => r.ValidationStatus != ReceiptValidationStatus.Invalid
            && (r.Status == ReceiptStatus.Signing || r.Status == ReceiptStatus.Returned || r.Status == ReceiptStatus.Completed
                || (r.Status == ReceiptStatus.Draft && r.Source == ReceiptSource.His)));

    public async Task MapAsync(WarehouseReceipt receipt, string? contractNumberHint, string? callOrderNumberHint, CancellationToken ct)
    {
        var supplierId = await db.Suppliers.Where(s => s.TaxCode == receipt.SupplierTaxCode).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (supplierId is null)
        {
            receipt.MapParties(null, null, null, null);
            return;
        }

        var contracts = db.Contracts.Include(c => c.Items).Where(c => c.SupplierId == supplierId && c.Status != ContractStatus.Cancelled);
        if (receipt.BidDecisionNumber is { } bidNumber)
        {
            var bidIds = db.BidDecisions.Where(b => b.Number == bidNumber).Select(b => b.Id);
            contracts = contracts.Where(c => c.BidDecisionId != null && bidIds.Contains(c.BidDecisionId.Value));
        }
        else
        {
            contracts = contracts.Where(c => c.ProcurementType == ProcurementType.NonBidding);
        }

        var candidates = await contracts.ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(contractNumberHint) && candidates.Any(c => c.Number == contractNumberHint.Trim()))
        {
            candidates = [.. candidates.Where(c => c.Number == contractNumberHint.Trim())];
        }

        var keys = receipt.Lines.Where(l => l.HisKey is not null).Select(l => l.HisKey!).Distinct().ToList();
        var itemsByKey = keys.Count == 0
            ? []
            : await db.Items.Where(i => i.HisKey != null && keys.Contains(i.HisKey)).Select(i => new { i.Id, i.HisKey }).ToListAsync(ct);

        ContractItem? Match(Contract contract, WarehouseReceiptLine line)
        {
            var itemIds = line.HisKey is null ? [] : itemsByKey.Where(i => i.HisKey == line.HisKey).Select(i => i.Id).ToList();
            return contract.Items.FirstOrDefault(ci => itemIds.Contains(ci.ItemId))
                ?? contract.Items.FirstOrDefault(ci => string.Equals(ci.ItemCode, line.ItemCode, StringComparison.OrdinalIgnoreCase));
        }

        // Prefer an effective contract, then the one covering most lines, then the latest.
        var contract = candidates
            .OrderBy(c => c.Status == ContractStatus.Effective ? 0 : c.Status == ContractStatus.Liquidated ? 1 : 2)
            .ThenByDescending(c => receipt.Lines.Count(l => Match(c, l) is not null))
            .ThenByDescending(c => c.EffectiveAt)
            .FirstOrDefault();
        if (contract is null)
        {
            receipt.MapParties(supplierId, null, null, null);
            return;
        }

        foreach (var line in receipt.Lines)
        {
            var ci = Match(contract, line);
            receipt.MapLine(line.LineNo, ci?.ItemId, ci?.Id);
        }

        receipt.MapParties(supplierId, contract.Id, contract.Number, await CallOrderAsync(receipt, supplierId.Value, callOrderNumberHint, ct));
    }

    public async Task ValidateAsync(WarehouseReceipt receipt, CancellationToken ct)
    {
        var supplierFound = receipt.SupplierId is not null;
        var contract = receipt.ContractId is { } cid
            ? await db.Contracts.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cid, ct)
            : null;

        var received = new Dictionary<Guid, decimal>();
        IReadOnlyDictionary<Guid, decimal>? outstanding = null;
        string? callOrderNumber = null;
        if (contract is not null)
        {
            received = await NetReceivedAsync(Counted(db.WarehouseReceipts).Where(r => r.ContractId == contract.Id && r.Id != receipt.Id), ct);
            if (receipt.CallOrderId is { } orderId)
            {
                var order = await db.CallOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct);
                if (order is not null)
                {
                    callOrderNumber = order.Number;
                    var delivered = await NetReceivedAsync(Counted(db.WarehouseReceipts).Where(r => r.CallOrderId == orderId && r.Id != receipt.Id), ct);
                    outstanding = order.Lines.GroupBy(l => l.ContractItemId)
                        .ToDictionary(g => g.Key, g => g.Sum(l => l.ConfirmedQuantity ?? l.Quantity) - delivered.GetValueOrDefault(g.Key));
                }
            }
        }

        var duplicates = receipt.HasInvoice
            ? await db.WarehouseReceipts.AsNoTracking()
                .Where(r => r.Id != receipt.Id && r.Status != ReceiptStatus.Cancelled && r.SupplierTaxCode == receipt.SupplierTaxCode
                    && r.InvoiceSymbol == receipt.InvoiceSymbol && r.InvoiceNumber == receipt.InvoiceNumber)
                .Select(r => r.Number).ToListAsync(ct)
            : [];

        var context = new ReceiptValidationContext
        {
            Receipt = receipt,
            SupplierFound = supplierFound,
            Contract = contract,
            ReceivedByOthers = received,
            CallOrderOutstanding = outstanding,
            CallOrderNumber = callOrderNumber,
            DuplicateInvoiceReceipts = duplicates,
            InvoiceRoundingTolerance = await settings.GetDecimalAsync(SystemSettingKeys.InvoiceRoundingTolerance, 1000, ct),
            ExpiryWarningDays = await settings.GetIntAsync(SystemSettingKeys.ExpiryWarningDays, 180, ct),
        };
        receipt.ApplyValidation(validator.Validate(context), clock.UtcNow);
    }

    /// <summary>Σ receipts − Σ returns by contract item.</summary>
    public static async Task<Dictionary<Guid, decimal>> NetReceivedAsync(IQueryable<WarehouseReceipt> receipts, CancellationToken ct) =>
        await receipts
            .SelectMany(r => r.Lines.Where(l => l.ContractItemId != null).Select(l => new { l.ContractItemId, Qty = r.Type == ReceiptType.Return ? -l.Quantity : l.Quantity }))
            .GroupBy(x => x.ContractItemId!.Value)
            .Select(g => new { g.Key, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    /// <summary>Explicit call order number, otherwise the oldest confirmed order of the supplier covering these items.</summary>
    private async Task<Guid?> CallOrderAsync(WarehouseReceipt receipt, Guid supplierId, string? hint, CancellationToken ct)
    {
        if (receipt.Type == ReceiptType.Return)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var byNumber = await db.CallOrders.Where(o => o.Number == hint.Trim() && o.SupplierId == supplierId).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
            if (byNumber is not null)
            {
                return byNumber;
            }
        }

        var itemIds = receipt.Lines.Where(l => l.ContractItemId is not null).Select(l => l.ContractItemId!.Value).ToList();
        if (itemIds.Count == 0)
        {
            return null;
        }

        return await db.CallOrders
            .Where(o => o.SupplierId == supplierId && (o.Status == CallOrderStatus.SupplierConfirmed || o.Status == CallOrderStatus.PartiallyConfirmed)
                && o.Lines.Any(l => itemIds.Contains(l.ContractItemId) && l.DeliveredQuantity < (l.ConfirmedQuantity ?? l.Quantity)))
            .OrderBy(o => o.CallDate).ThenBy(o => o.CreatedAt)
            .Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
    }
}
