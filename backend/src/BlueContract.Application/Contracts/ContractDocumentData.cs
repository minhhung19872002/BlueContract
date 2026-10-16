using System.Globalization;
using System.Text.Json.Nodes;
using BlueContract.Application.Common;
using BlueContract.Domain.Bids;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Contracts;

/// <summary>
/// Builds the template data of contracts and appendices with exactly the paths of <see cref="Documents.TemplateFieldCatalog"/>.
/// Dates are ISO strings (formatted by the template filters), money/quantities are numbers.
/// </summary>
public sealed class ContractDocumentData(IAppDbContext db, IClock clock)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<JsonObject> BuildAsync(Contract contract, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(ct);
        // New suppliers/items of a contract being created are not saved yet: look in the unit of work first.
        var supplier = db.Suppliers.Local.FirstOrDefault(s => s.Id == contract.SupplierId)
            ?? await db.Suppliers.AsNoTracking().IgnoreQueryFilters().FirstAsync(s => s.Id == contract.SupplierId, ct);
        var category = await db.ItemCategories.AsNoTracking().FirstAsync(c => c.Id == contract.CategoryId, ct);
        var facility = contract.FacilityId is { } fid ? await db.Facilities.AsNoTracking().Where(f => f.Id == fid).Select(f => f.Name).FirstOrDefaultAsync(ct) : null;
        var bid = contract.BidDecisionId is { } bid1 ? await db.BidDecisions.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bid1, ct) : null;
        var itemIds = contract.Items.Select(i => i.ItemId).ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var local in db.Items.Local.Where(i => itemIds.Contains(i.Id)))
        {
            items[local.Id] = local;
        }
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(VietnamOffset).DateTime);

        var data = new JsonObject
        {
            ["Organization"] = Organization(org),
            ["Supplier"] = Supplier(supplier),
            ["Document"] = new JsonObject
            {
                ["Number"] = contract.Number,
                ["Date"] = Iso(contract.SignedDate ?? today),
                ["Place"] = Place(org?.Address),
            },
            ["Today"] = Iso(today),
            ["Contract"] = ContractNode(contract, category, facility, bid),
            ["BidDecision"] = new JsonObject
            {
                ["Number"] = bid?.Number ?? string.Empty,
                ["Date"] = bid is null ? string.Empty : Iso(bid.SignedDate),
                ["PackageName"] = bid?.PackageName ?? string.Empty,
                ["SelectionMethod"] = bid?.SelectionMethod ?? string.Empty,
            },
            ["Items"] = new JsonArray([.. contract.Items.OrderBy(i => i.ItemCode).Select(i => (JsonNode)ItemNode(i, items.GetValueOrDefault(i.ItemId)))]),
        };
        return data;
    }

    public async Task<JsonObject> BuildAppendixAsync(Contract contract, ContractAppendix appendix, CancellationToken ct)
    {
        var data = await BuildAsync(contract, ct);
        data["Document"]!["Number"] = $"{appendix.Number}/{contract.Number}";
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(VietnamOffset).DateTime);
        data["Document"]!["Date"] = Iso(today);
        data["Appendix"] = new JsonObject
        {
            ["Number"] = appendix.Number,
            ["Date"] = Iso(today),
            ["TypeText"] = TypeText(appendix.Type),
            ["Reason"] = appendix.Reason,
            ["NewExpiryDate"] = appendix.NewExpiryDate is { } d ? Iso(d) : string.Empty,
            ["ValueChange"] = appendix.ValueChange,
            ["NewTotalAmount"] = contract.TotalAmount + appendix.ValueChange,
        };

        var vi = CultureInfo.GetCultureInfo("vi-VN");
        var changes = new JsonArray();
        foreach (var line in appendix.Lines)
        {
            if (line.NewQuantity is { } q)
            {
                changes.Add(new JsonObject { ["ItemName"] = line.ItemName, ["Field"] = "Số lượng", ["OldValue"] = line.OldQuantity.ToString("#,##0.###", vi), ["NewValue"] = q.ToString("#,##0.###", vi) });
            }

            if (line.NewUnitPrice is { } p)
            {
                changes.Add(new JsonObject { ["ItemName"] = line.ItemName, ["Field"] = "Đơn giá (đồng)", ["OldValue"] = line.OldUnitPrice.ToString("#,##0", vi), ["NewValue"] = p.ToString("#,##0", vi) });
            }
        }

        if (appendix.NewExpiryDate is { } expiry)
        {
            changes.Add(new JsonObject
            {
                ["ItemName"] = "Thời hạn hợp đồng",
                ["Field"] = "Ngày hết hạn",
                ["OldValue"] = appendix.OldExpiryDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                ["NewValue"] = expiry.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            });
        }

        if (appendix.InfoChanges is { } info)
        {
            changes.Add(new JsonObject { ["ItemName"] = "Thông tin hợp đồng", ["Field"] = "Nội dung thay đổi", ["OldValue"] = string.Empty, ["NewValue"] = info });
        }

        data["Changes"] = changes;
        return data;
    }

    public static string TypeText(AppendixType type) => type switch
    {
        AppendixType.AddQuantity => "Bổ sung số lượng",
        AppendixType.PriceReduction => "Giảm giá",
        AppendixType.InfoChange => "Thay đổi thông tin",
        _ => "Gia hạn thời gian thực hiện",
    };

    private static JsonObject ContractNode(Contract c, ItemCategory category, string? facility, BidDecision? bid) => new()
    {
        ["Number"] = c.Number,
        ["Name"] = c.Name,
        ["SignedDate"] = c.SignedDate is { } s ? Iso(s) : string.Empty,
        ["EffectiveDate"] = c.EffectiveDate is { } e ? Iso(e) : string.Empty,
        ["ExpiryDate"] = c.ExpiryDate is { } x ? Iso(x) : string.Empty,
        ["Duration"] = c.Duration ?? bid?.ExecutionPeriod ?? string.Empty,
        ["ProcurementTypeText"] = c.ProcurementType == ProcurementType.Bidding ? "Trong thầu" : "Ngoài thầu",
        ["IsBidding"] = c.ProcurementType == ProcurementType.Bidding,
        ["CategoryText"] = category.Name,
        ["FacilityName"] = facility ?? "Toàn bệnh viện",
        ["AmountBeforeVat"] = c.AmountBeforeVat,
        ["VatAmount"] = c.VatAmount,
        ["TotalAmount"] = c.TotalAmount,
        ["PaymentTerms"] = c.PaymentTerms ?? string.Empty,
        ["WarrantyTerms"] = c.WarrantyTerms ?? string.Empty,
        ["DeliveryTerms"] = c.DeliveryTerms ?? string.Empty,
        ["GuaranteeText"] = GuaranteeText(c),
        ["FundingSource"] = bid?.FundingSource ?? string.Empty,
    };

    private static JsonObject ItemNode(ContractItem i, Item? item) => new()
    {
        ["Code"] = i.ItemCode,
        ["Name"] = i.ItemName,
        ["ActiveIngredient"] = Join(item?.ActiveIngredient, item?.Strength),
        ["Specification"] = item?.Specification ?? string.Empty,
        ["Unit"] = i.Unit,
        ["Manufacturer"] = item?.Manufacturer ?? string.Empty,
        ["Country"] = item?.Country ?? string.Empty,
        ["RegistrationNumber"] = item?.RegistrationNumber ?? string.Empty,
        ["BidGroup"] = item?.BidGroup ?? string.Empty,
        ["Quantity"] = i.Quantity,
        ["UnitPrice"] = i.UnitPrice,
        ["VatRate"] = i.VatRate,
        ["Amount"] = i.Amount,
    };

    private static JsonObject Organization(Organization? o) => new()
    {
        ["Name"] = o?.Name ?? string.Empty,
        ["ShortName"] = o?.ShortName ?? string.Empty,
        ["TaxCode"] = o?.TaxCode ?? string.Empty,
        ["Address"] = o?.Address ?? string.Empty,
        ["Phone"] = o?.Phone ?? string.Empty,
        ["RepresentativeName"] = o?.RepresentativeName ?? string.Empty,
        ["RepresentativeTitle"] = o?.RepresentativeTitle ?? string.Empty,
        ["BankAccountNumber"] = o?.BankAccountNumber ?? string.Empty,
        ["BankName"] = o?.BankName ?? string.Empty,
        ["TreasuryAccountNumber"] = o?.TreasuryAccountNumber ?? string.Empty,
        ["TreasuryName"] = o?.TreasuryName ?? string.Empty,
    };

    private static JsonObject Supplier(Supplier s) => new()
    {
        ["Name"] = s.Name,
        ["TaxCode"] = s.TaxCode,
        ["Address"] = s.Address ?? string.Empty,
        ["Phone"] = s.Phone ?? string.Empty,
        ["Email"] = s.NotificationEmail ?? string.Empty,
        ["RepresentativeName"] = s.RepresentativeName ?? string.Empty,
        ["RepresentativeTitle"] = s.RepresentativeTitle ?? string.Empty,
        ["AuthorizationDocument"] = s.AuthorizationDocument ?? string.Empty,
        ["BankAccountNumber"] = s.BankAccountNumber ?? string.Empty,
        ["BankName"] = s.BankName ?? string.Empty,
    };

    private static string GuaranteeText(Contract c)
    {
        if (c.GuaranteeNumber is null && c.GuaranteeAmount is null)
        {
            return string.Empty;
        }

        var vi = CultureInfo.GetCultureInfo("vi-VN");
        var parts = new List<string> { "Bảo lãnh thực hiện hợp đồng" };
        if (c.GuaranteeNumber is not null)
        {
            parts.Add($"số {c.GuaranteeNumber}");
        }

        if (c.GuaranteeBank is not null)
        {
            parts.Add($"do {c.GuaranteeBank} phát hành");
        }

        if (c.GuaranteeAmount is { } amount)
        {
            parts.Add($"giá trị {amount.ToString("#,##0", vi)} đồng");
        }

        if (c.GuaranteeExpiry is { } expiry)
        {
            parts.Add($"có hiệu lực đến ngày {expiry.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");
        }

        return string.Join(", ", parts) + ".";
    }

    /// <summary>"Thành phố Vinh, tỉnh Nghệ An" → "Nghệ An" (địa danh ghi trên văn bản).</summary>
    public static string Place(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        var last = address.Split(',').Last().Trim();
        foreach (var prefix in new[] { "tỉnh ", "Tỉnh ", "thành phố ", "Thành phố ", "TP. ", "TP " })
        {
            if (last.StartsWith(prefix, StringComparison.Ordinal))
            {
                return last[prefix.Length..].Trim();
            }
        }

        return last;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Join(string? a, string? b) =>
        string.Join(" ", new[] { a, b }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
