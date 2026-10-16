using System.Globalization;
using System.Text.Json.Nodes;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Domain.Proposals;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Proposals;

/// <summary>Template data of Phiếu đề xuất, Phiếu tổng hợp đề xuất and Đơn gọi hàng (paths of TemplateFieldCatalog).</summary>
public sealed class ProposalDocumentData(IAppDbContext db, ContractDocumentData contracts, IClock clock)
{
    private DateOnly LocalToday => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public async Task<JsonObject> ProposalAsync(Proposal p, CancellationToken ct)
    {
        var data = await CommonAsync(p.Number, ct);
        var department = await db.Departments.AsNoTracking().Where(d => d.Id == p.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(ct);
        var facility = p.FacilityId is { } fid ? await db.Facilities.AsNoTracking().Where(f => f.Id == fid).Select(f => f.Name).FirstOrDefaultAsync(ct) : null;
        data["Proposal"] = new JsonObject
        {
            ["Number"] = p.Number,
            ["Date"] = Iso(LocalToday),
            ["Period"] = p.PeriodText,
            ["DepartmentName"] = department ?? string.Empty,
            ["FacilityName"] = facility ?? string.Empty,
            ["DomainText"] = ProposalScope.DomainText(p.Domain),
            ["CreatedByName"] = p.CreatedByName ?? string.Empty,
            ["Note"] = p.Note ?? string.Empty,
        };
        data["Lines"] = new JsonArray([.. p.Lines.OrderBy(l => l.ContractNumber).ThenBy(l => l.ItemName).Select(l => (JsonNode)new JsonObject
        {
            ["ItemName"] = l.ItemName,
            ["Unit"] = l.Unit,
            ["ContractNumber"] = l.ContractNumber,
            ["OpeningStock"] = l.OpeningStock,
            ["Received"] = l.Received,
            ["Issued"] = l.Issued,
            ["CurrentStock"] = l.CurrentStock,
            ["ContractRemaining"] = l.ContractRemaining,
            ["ProposedQuantity"] = l.ProposedQuantity,
            ["Note"] = l.Note ?? string.Empty,
        })]);
        return data;
    }

    public async Task<JsonObject> SummaryAsync(ProposalSummary s, CancellationToken ct)
    {
        var data = await CommonAsync(s.Number, ct);
        data["Summary"] = new JsonObject
        {
            ["Number"] = s.Number,
            ["Date"] = Iso(LocalToday),
            ["Period"] = s.PeriodText,
            ["DomainText"] = ProposalScope.DomainText(s.Domain),
            ["AggregatorName"] = s.AggregatorName ?? string.Empty,
            ["ProposalCount"] = s.Sources.Count,
        };
        data["Lines"] = new JsonArray([.. s.Lines.OrderBy(l => l.SupplierName).ThenBy(l => l.ItemName).Select(l => (JsonNode)new JsonObject
        {
            ["ItemName"] = l.ItemName,
            ["Unit"] = l.Unit,
            ["SupplierName"] = l.SupplierName,
            ["ContractNumber"] = l.ContractNumber,
            ["ProposedQuantity"] = l.ProposedQuantity,
            ["OpeningStock"] = l.OpeningStock,
            ["Received"] = l.Received,
            ["Issued"] = l.Issued,
            ["CurrentStock"] = l.CurrentStock,
            ["ApprovedQuantity"] = l.ApprovedQuantity,
            ["UnitPrice"] = l.UnitPrice,
            ["Amount"] = l.Amount,
        })]);
        return data;
    }

    /// <summary>Đơn gọi hàng: contract fields come from the order's (first) contract — orders are split per supplier.</summary>
    public async Task<JsonObject> CallOrderAsync(CallOrder o, string summaryNumber, CancellationToken ct)
    {
        var contractId = o.Lines.First().ContractId;
        var contract = await db.Contracts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == contractId, ct);
        var data = await contracts.BuildAsync(contract, ct);
        data["Document"]!["Number"] = o.Number;
        data["Document"]!["Date"] = Iso(o.CallDate);
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == o.WarehouseId)
            .Select(w => new { w.Name, Facility = db.Facilities.Where(f => f.Id == w.FacilityId).Select(f => f.Address ?? f.Name).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        data["CallOrder"] = new JsonObject
        {
            ["Number"] = o.Number,
            ["Date"] = Iso(o.CallDate),
            ["DeliveryDeadline"] = Iso(o.DeliveryDeadline),
            ["WarehouseName"] = warehouse?.Name ?? string.Empty,
            ["WarehouseAddress"] = warehouse?.Facility ?? string.Empty,
            ["SummaryNumber"] = summaryNumber,
            ["TotalAmount"] = o.TotalAmount,
            ["ContactName"] = o.ContactName ?? string.Empty,
        };
        data["Lines"] = new JsonArray([.. o.Lines.OrderBy(l => l.ItemName).Select(l => (JsonNode)new JsonObject
        {
            ["ItemName"] = l.ContractNumber == contract.Number ? l.ItemName : $"{l.ItemName} (HĐ {l.ContractNumber})",
            ["Unit"] = l.Unit,
            ["Quantity"] = l.Quantity,
            ["UnitPrice"] = l.UnitPrice,
            ["Amount"] = l.Amount,
        })]);
        return data;
    }

    private async Task<JsonObject> CommonAsync(string number, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(ct);
        return new JsonObject
        {
            ["Organization"] = new JsonObject
            {
                ["Name"] = org?.Name ?? string.Empty,
                ["ShortName"] = org?.ShortName ?? string.Empty,
                ["TaxCode"] = org?.TaxCode ?? string.Empty,
                ["Address"] = org?.Address ?? string.Empty,
                ["Phone"] = org?.Phone ?? string.Empty,
                ["RepresentativeName"] = org?.RepresentativeName ?? string.Empty,
                ["RepresentativeTitle"] = org?.RepresentativeTitle ?? string.Empty,
                ["BankAccountNumber"] = org?.BankAccountNumber ?? string.Empty,
                ["BankName"] = org?.BankName ?? string.Empty,
                ["TreasuryAccountNumber"] = org?.TreasuryAccountNumber ?? string.Empty,
                ["TreasuryName"] = org?.TreasuryName ?? string.Empty,
            },
            ["Supplier"] = new JsonObject(),
            ["Document"] = new JsonObject { ["Number"] = number, ["Date"] = Iso(LocalToday), ["Place"] = ContractDocumentData.Place(org?.Address) },
            ["Today"] = Iso(LocalToday),
        };
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
