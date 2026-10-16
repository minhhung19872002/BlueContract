using System.Globalization;
using System.Text.Json.Nodes;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Domain.Receipts;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Receipts;

/// <summary>Template data of the Phiếu nhập kho / Phiếu hoàn trả (TemplateFieldCatalog: Receipt.*, Lines).</summary>
public sealed class ReceiptDocumentData(IAppDbContext db, ContractDocumentData contracts)
{
    public async Task<JsonObject> BuildAsync(WarehouseReceipt r, CancellationToken ct)
    {
        JsonObject data;
        var contract = r.ContractId is { } cid ? await db.Contracts.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cid, ct) : null;
        if (contract is not null)
        {
            data = await contracts.BuildAsync(contract, ct);
        }
        else
        {
            var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(ct);
            var supplier = r.SupplierId is { } sid ? await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct) : null;
            data = new JsonObject
            {
                ["Organization"] = new JsonObject { ["Name"] = org?.Name ?? string.Empty, ["Address"] = org?.Address ?? string.Empty },
                ["Supplier"] = new JsonObject { ["Name"] = supplier?.Name ?? string.Empty, ["TaxCode"] = r.SupplierTaxCode },
                ["Document"] = new JsonObject { ["Place"] = ContractDocumentData.Place(org?.Address) },
            };
        }

        data["Document"]!["Number"] = r.Number;
        data["Document"]!["Date"] = Iso(r.ReceiptDate);
        var warehouse = r.WarehouseId is { } wid ? await db.Warehouses.AsNoTracking().Where(w => w.Id == wid).Select(w => w.Name).FirstOrDefaultAsync(ct) : null;
        data["Receipt"] = new JsonObject
        {
            ["Number"] = r.Number,
            ["Date"] = Iso(r.ReceiptDate),
            ["TypeText"] = r.IsVirtual ? "PHIẾU XÁC NHẬN KHỐI LƯỢNG" : r.Type == ReceiptType.Return ? "PHIẾU HOÀN TRẢ" : "PHIẾU NHẬP KHO",
            ["IsReturn"] = r.Type == ReceiptType.Return,
            ["IsVirtual"] = r.IsVirtual,
            ["WarehouseName"] = warehouse ?? r.WarehouseCode,
            ["DelivererName"] = r.DelivererName ?? string.Empty,
            ["BidDecisionNumber"] = r.BidDecisionNumber ?? string.Empty,
            ["ContractNumber"] = r.ContractNumber ?? string.Empty,
            ["InvoiceSymbol"] = r.InvoiceSymbol ?? "…………",
            ["InvoiceNumber"] = r.InvoiceNumber ?? "…………",
            ["InvoiceDate"] = r.InvoiceDate is { } d ? Iso(d) : string.Empty,
            ["AmountBeforeVat"] = r.TotalAmount - r.VatAmount,
            ["VatAmount"] = r.VatAmount,
            ["TotalAmount"] = r.TotalAmount,
        };
        data["Lines"] = new JsonArray([.. r.Lines.OrderBy(l => l.LineNo).Select(l => (JsonNode)new JsonObject
        {
            ["ItemCode"] = l.ItemCode,
            ["ItemName"] = l.ItemName,
            ["Unit"] = l.Unit,
            ["LotNumber"] = l.LotNumber ?? string.Empty,
            ["ExpiryDate"] = l.ExpiryDate is { } e ? Iso(e) : string.Empty,
            ["Quantity"] = l.Quantity,
            ["UnitPrice"] = l.UnitPrice,
            ["Amount"] = l.Amount,
        })]);
        return data;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
