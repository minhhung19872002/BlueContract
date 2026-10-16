using BlueContract.Application.Common;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Integration;

public sealed record HisContractItemDto(
    string? HisKey,
    string ItemCode,
    string ItemName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal CalledQuantity,
    decimal ReceivedQuantity,
    decimal RemainingQuantity);

public sealed record HisContractDto(
    string ContractNumber,
    string Name,
    string? BidDecisionNumber,
    string SupplierTaxCode,
    string SupplierName,
    ContractStatus Status,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    decimal TotalAmount,
    IReadOnlyList<HisContractItemDto> Items);

public sealed record HisSummaryLineDto(
    string ContractNumber,
    string SupplierTaxCode,
    string? HisKey,
    string ItemCode,
    string ItemName,
    string Unit,
    decimal UnitPrice,
    decimal ApprovedQuantity,
    decimal CalledQuantity,
    decimal RemainingToCall);

public sealed record HisSummaryDto(string Number, ProcurementDomain Domain, string PeriodText, ProposalSummaryStatus Status, DateTimeOffset? ApprovedAt, IReadOnlyList<HisSummaryLineDto> Lines);

public sealed record HisItemDto(
    string? HisKey,
    string Code,
    string Name,
    string Unit,
    string? ActiveIngredient,
    string? Strength,
    string? Specification,
    string? Manufacturer,
    string? Country,
    string? RegistrationNumber,
    string CategoryCode,
    string? BidDecisionNumber,
    string? SupplierTaxCode,
    decimal? WinningUnitPrice,
    bool IsActive,
    DateTimeOffset UpdatedAt);

/// <summary>Read APIs the portal offers to the HIS (spec §7.2): contracts with remaining quantities, approved summaries, items.</summary>
public sealed class HisInboundService(IAppDbContext db)
{
    public const int MaxRows = 5000;

    public async Task<List<HisContractDto>> ContractsAsync(string? bidDecisionNumber, string? supplierTaxCode, CancellationToken ct)
    {
        var query = db.Contracts.AsNoTracking().Where(c => c.Status == ContractStatus.Effective);
        if (!string.IsNullOrWhiteSpace(bidDecisionNumber))
        {
            var bidIds = db.BidDecisions.Where(b => b.Number == bidDecisionNumber.Trim()).Select(b => b.Id);
            query = query.Where(c => c.BidDecisionId != null && bidIds.Contains(c.BidDecisionId.Value));
        }

        if (!string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            var supplierIds = db.Suppliers.Where(s => s.TaxCode == supplierTaxCode.Trim()).Select(s => s.Id);
            query = query.Where(c => supplierIds.Contains(c.SupplierId));
        }

        var rows = await query.OrderBy(c => c.Number).Take(MaxRows)
            .Select(c => new
            {
                Contract = c,
                c.Items,
                Bid = db.BidDecisions.Where(b => b.Id == c.BidDecisionId).Select(b => b.Number).FirstOrDefault(),
                Supplier = db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => new { s.TaxCode, s.Name }).First(),
            })
            .ToListAsync(ct);
        var itemIds = rows.SelectMany(r => r.Items).Select(i => i.ItemId).Distinct().ToList();
        var keys = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.HisKey, ct);
        return [.. rows.Select(r => new HisContractDto(r.Contract.Number, r.Contract.Name, r.Bid, r.Supplier.TaxCode, r.Supplier.Name, r.Contract.Status,
            r.Contract.EffectiveDate, r.Contract.ExpiryDate, r.Contract.TotalAmount,
            [.. r.Items.OrderBy(i => i.ItemCode).Select(i => new HisContractItemDto(keys.GetValueOrDefault(i.ItemId), i.ItemCode, i.ItemName, i.Unit, i.Quantity,
                i.UnitPrice, i.VatRate, i.CalledQuantity, i.ReceivedQuantity, i.RemainingQuantity))]))];
    }

    public async Task<List<HisSummaryDto>> SummariesAsync(ProposalSummaryStatus? status, DateTimeOffset? approvedSince, CancellationToken ct)
    {
        var effective = status ?? ProposalSummaryStatus.Approved;
        var query = db.ProposalSummaries.AsNoTracking().Include(s => s.Lines).Where(s => s.Status == effective);
        if (approvedSince is { } since)
        {
            query = query.Where(s => s.ApprovedAt >= since);
        }

        var summaries = await query.OrderByDescending(s => s.ApprovedAt).Take(500).ToListAsync(ct);
        var supplierIds = summaries.SelectMany(s => s.Lines).Select(l => l.SupplierId).Distinct().ToList();
        var taxCodes = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.TaxCode, ct);
        var contractItemIds = summaries.SelectMany(s => s.Lines).Select(l => l.ContractItemId).Distinct().ToList();
        var keys = await db.Contracts.AsNoTracking().SelectMany(c => c.Items).Where(i => contractItemIds.Contains(i.Id))
            .Select(i => new { i.Id, Key = db.Items.Where(x => x.Id == i.ItemId).Select(x => x.HisKey).FirstOrDefault() })
            .ToDictionaryAsync(x => x.Id, x => x.Key, ct);
        return [.. summaries.Select(s => new HisSummaryDto(s.Number, s.Domain, s.PeriodText, s.Status, s.ApprovedAt,
            [.. s.Lines.OrderBy(l => l.ContractNumber).ThenBy(l => l.ItemCode).Select(l => new HisSummaryLineDto(l.ContractNumber, taxCodes.GetValueOrDefault(l.SupplierId) ?? string.Empty,
                keys.GetValueOrDefault(l.ContractItemId), l.ItemCode, l.ItemName, l.Unit, l.UnitPrice, l.ApprovedQuantity, l.CalledQuantity,
                l.ApprovedQuantity - l.CalledQuantity))]))];
    }

    public async Task<List<HisItemDto>> ItemsAsync(DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking();
        if (updatedSince is { } since)
        {
            query = query.Where(i => (i.UpdatedAt ?? i.CreatedAt) >= since);
        }

        return await query.OrderBy(i => i.Code).Take(MaxRows)
            .Select(i => new HisItemDto(i.HisKey, i.Code, i.Name, i.Unit, i.ActiveIngredient, i.Strength, i.Specification, i.Manufacturer, i.Country,
                i.RegistrationNumber,
                db.ItemCategories.Where(c => c.Id == i.CategoryId).Select(c => c.Code).FirstOrDefault() ?? string.Empty,
                db.BidDecisions.Where(b => b.Id == i.BidDecisionId).Select(b => b.Number).FirstOrDefault(),
                db.Suppliers.Where(s => s.Id == i.SupplierId).Select(s => s.TaxCode).FirstOrDefault(),
                i.WinningUnitPrice, i.IsActive, i.UpdatedAt ?? i.CreatedAt))
            .ToListAsync(ct);
    }
}
