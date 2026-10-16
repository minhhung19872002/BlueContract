using System.Globalization;
using System.Text.Json.Nodes;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Payments;

/// <summary>Template data of BB nghiệm thu, Mẫu 08a, Đề nghị thanh toán và BB thanh lý (paths of TemplateFieldCatalog).</summary>
public sealed class PaymentDocumentData(IAppDbContext db, ContractDocumentData contracts, IClock clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public async Task<JsonObject> AcceptanceAsync(AcceptanceRecord a, CancellationToken ct)
    {
        var data = await BaseAsync(a.ContractId, a.Number, ct);
        data["Acceptance"] = new JsonObject
        {
            ["Number"] = a.Number,
            ["Date"] = Iso(Today),
            ["PeriodText"] = a.PeriodText,
            ["TotalAmount"] = a.TotalAmount,
            ["Conclusion"] = a.Conclusion ?? "Hàng hóa giao đúng chủng loại, số lượng, chất lượng theo hợp đồng; đồng ý nghiệm thu.",
        };
        data["Receipts"] = new JsonArray([.. a.Receipts.OrderBy(r => r.Date).ThenBy(r => r.Number).Select(r => (JsonNode)new JsonObject
        {
            ["Number"] = r.Number,
            ["Date"] = Iso(r.Date),
            ["InvoiceNumber"] = r.InvoiceNumber ?? string.Empty,
            ["Amount"] = r.Amount,
        })]);
        data["Items"] = new JsonArray([.. a.Items.OrderBy(i => i.ItemCode).Select(i => (JsonNode)new JsonObject
        {
            ["Name"] = i.ItemName,
            ["Unit"] = i.Unit,
            ["Quantity"] = i.Quantity,
            ["UnitPrice"] = i.UnitPrice,
            ["Amount"] = i.Amount,
        })]);
        return data;
    }

    public async Task<JsonObject> ValuationAsync(ValuationRecord v, CancellationToken ct)
    {
        var data = await BaseAsync(v.ContractId, v.Number, ct);
        data["Valuation"] = new JsonObject
        {
            ["Number"] = v.Number,
            ["Date"] = Iso(Today),
            ["PeriodText"] = v.PeriodText,
            ["PaymentNumber"] = v.PaymentSequence,
            ["ContractValue"] = v.ContractValue,
            ["AdvanceAmount"] = v.AdvanceAmount,
            ["PreviousCumulative"] = v.PreviousCumulative,
            ["CurrentPeriodValue"] = v.CurrentPeriodValue,
            ["CumulativeToDate"] = v.CumulativeToDate,
            ["CompletionPercent"] = v.CompletionPercent,
            ["AdvanceRecovered"] = v.AdvanceRecovered,
            ["RequestedAmount"] = v.RequestedAmount,
        };
        data["Items"] = new JsonArray([.. v.Items.OrderBy(i => i.ItemName).Select(i => (JsonNode)new JsonObject
        {
            ["Name"] = i.ItemName,
            ["Unit"] = i.Unit,
            ["ContractQuantity"] = i.ContractQuantity,
            ["UnitPrice"] = i.UnitPrice,
            ["PreviousQuantity"] = i.PreviousQuantity,
            ["CurrentQuantity"] = i.CurrentQuantity,
            ["CumulativeQuantity"] = i.CumulativeQuantity,
            ["ContractAmount"] = i.ContractAmount,
            ["PreviousAmount"] = i.PreviousAmount,
            ["CurrentAmount"] = i.CurrentAmount,
            ["CumulativeAmount"] = i.CumulativeAmount,
        })]);
        return data;
    }

    public async Task<JsonObject> PaymentRequestAsync(ValuationRecord v, decimal paidBefore, CancellationToken ct)
    {
        var data = await BaseAsync(v.ContractId, v.PaymentRequestNumber, ct);
        data["Payment"] = new JsonObject
        {
            ["Number"] = v.PaymentRequestNumber,
            ["Date"] = Iso(Today),
            ["ValuationNumber"] = v.Number,
            ["RequestedAmount"] = v.RequestedAmount,
            ["PaidBefore"] = paidBefore,
            ["AdvanceRecovered"] = v.AdvanceRecovered,
            ["PaymentOrderNumber"] = string.Join(", ", v.PaymentOrders.Select(p => p.Number)),
        };
        return data;
    }

    public async Task<JsonObject> LiquidationAsync(LiquidationRecord l, CancellationToken ct)
    {
        var data = await BaseAsync(l.ContractId, l.Number, ct);
        data["Liquidation"] = new JsonObject
        {
            ["Number"] = l.Number,
            ["Date"] = Iso(Today),
            ["ContractValue"] = l.ContractValue,
            ["ExecutedValue"] = l.ExecutedValue,
            ["PaidAmount"] = l.PaidAmount + l.SettledAmount,
            ["RemainingAmount"] = l.RemainingAmount,
            ["ReductionAmount"] = l.ReductionAmount,
            ["Notes"] = Notes(l),
        };
        data["Valuations"] = new JsonArray([.. l.Valuations.OrderBy(v => v.Date).ThenBy(v => v.Number).Select(v => (JsonNode)new JsonObject
        {
            ["Number"] = v.Number,
            ["Date"] = Iso(v.Date),
            ["Amount"] = v.Amount,
            ["PaidAmount"] = v.PaidAmount + l.Settlements.Where(s => s.ValuationId == v.ValuationId).Sum(s => s.Amount),
            ["PaymentOrderNumber"] = v.PaymentOrderNumbers ?? string.Empty,
        })]);
        return data;
    }

    private static string Notes(LiquidationRecord l)
    {
        var vi = CultureInfo.GetCultureInfo("vi-VN");
        var parts = new List<string>();
        if (l.Notes is not null)
        {
            parts.Add(l.Notes);
        }

        parts.AddRange(l.Settlements.Select(s =>
            $"Chốt công nợ: {s.Description} — {s.Amount.ToString("#,##0", vi)} đồng{(s.VoucherNumber is null ? string.Empty : $", chứng từ số {s.VoucherNumber}")}"
            + $"{(s.VoucherDate is { } d ? $" ngày {d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" : string.Empty)}."));
        return string.Join(" ", parts);
    }

    private async Task<JsonObject> BaseAsync(Guid contractId, string number, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == contractId, ct);
        var data = await contracts.BuildAsync(contract, ct);
        data["Document"]!["Number"] = number;
        data["Document"]!["Date"] = Iso(Today);
        return data;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
