using System.Globalization;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using BlueContract.Application.Common;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Catalog;

public sealed record ItemCategoryDto(Guid Id, string Code, string Name, ProcurementDomain Domain, ItemImportKind ImportKind, string? DefaultContractTemplateCode);

public sealed record ItemDto(
    Guid Id,
    string? HisKey,
    string Code,
    string Name,
    string? ActiveIngredient,
    string? Strength,
    string? Specification,
    string Unit,
    string? Manufacturer,
    string? Country,
    string? RegistrationNumber,
    string? BidGroup,
    Guid CategoryId,
    string CategoryName,
    Guid? SupplierId,
    string? SupplierName,
    Guid? BidDecisionId,
    string? BidDecisionNumber,
    decimal? WinningUnitPrice,
    ItemSource Source,
    bool IsActive);

public sealed record ItemFilter(Guid? CategoryId, ItemSource? Source, Guid? SupplierId, Guid? BidDecisionId, bool? IsActive);

public sealed record SaveItemRequest(
    string Code,
    string Name,
    string Unit,
    Guid CategoryId,
    string? HisKey,
    string? ActiveIngredient,
    string? Strength,
    string? Specification,
    string? Manufacturer,
    string? Country,
    string? RegistrationNumber,
    string? BidGroup,
    Guid? SupplierId,
    bool IsActive = true);

internal sealed class SaveItemRequestValidator : AbstractValidator<SaveItemRequest>
{
    public SaveItemRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("Mã hàng").MaximumLength(60);
        RuleFor(x => x.Name).NotEmpty().WithName("Tên hàng").MaximumLength(500);
        RuleFor(x => x.Unit).NotEmpty().WithName("Đơn vị tính").MaximumLength(40);
        RuleFor(x => x.CategoryId).NotEmpty().WithName("Hạng mục");
    }
}

/// <summary>Danh mục hàng hóa (spec §9.1-4): categories, items by category with their source, manual entry.</summary>
public sealed class CatalogService(IAppDbContext db, IItemWorkbook workbook)
{
    private static readonly Dictionary<string, Expression<Func<Item, object?>>> Sorts = new()
    {
        ["code"] = x => x.Code,
        ["name"] = x => x.Name,
        ["winningUnitPrice"] = x => x.WinningUnitPrice,
        ["createdAt"] = x => x.CreatedAt,
    };

    public Task<List<ItemCategoryDto>> CategoriesAsync(CancellationToken ct) =>
        db.ItemCategories.AsNoTracking().OrderBy(c => c.SortOrder)
            .Select(c => new ItemCategoryDto(c.Id, c.Code, c.Name, c.Domain, c.ImportKind, c.DefaultContractTemplateCode))
            .ToListAsync(ct);

    public Task<PagedResult<ItemDto>> ListAsync(PageQuery q, ItemFilter f, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(i => EF.Functions.ILike(i.Code, p) || EF.Functions.ILike(i.Name, p) || EF.Functions.ILike(i.HisKey!, p)
                || EF.Functions.ILike(i.ActiveIngredient!, p));
        }

        if (f.CategoryId is not null)
        {
            query = query.Where(i => i.CategoryId == f.CategoryId);
        }

        if (f.Source is not null)
        {
            query = query.Where(i => i.Source == f.Source);
        }

        if (f.SupplierId is not null)
        {
            query = query.Where(i => i.SupplierId == f.SupplierId);
        }

        if (f.BidDecisionId is not null)
        {
            query = query.Where(i => i.BidDecisionId == f.BidDecisionId);
        }

        if (f.IsActive is not null)
        {
            query = query.Where(i => i.IsActive == f.IsActive);
        }

        return Project(query.ApplySort(q, Sorts, "code")).ToPagedAsync(q, ct);
    }

    public async Task<Result<ItemDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await Project(db.Items.AsNoTracking().Where(i => i.Id == id)).FirstOrDefaultAsync(ct);
        return dto is null ? Error.NotFound() : dto;
    }

    /// <summary>Nhập tay (items outside the HIS, e.g. services of non-bidding contracts).</summary>
    public async Task<Result<ItemDto>> CreateAsync(SaveItemRequest r, CancellationToken ct)
    {
        if (!await db.ItemCategories.AnyAsync(c => c.Id == r.CategoryId, ct))
        {
            return Error.Validation("ITEM_CATEGORY_NOT_FOUND");
        }

        if (await DuplicateAsync(null, r, ct))
        {
            return Error.Conflict("ITEM_CODE_EXISTS");
        }

        var item = new Item(r.Code, r.Name, r.Unit, r.CategoryId, ItemSource.Manual);
        Apply(item, r);
        if (r.SupplierId is { } supplierId)
        {
            item.AssignToSupplier(supplierId);
        }

        db.Items.Add(item);
        await db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, ct);
    }

    public async Task<Result<ItemDto>> UpdateAsync(Guid id, SaveItemRequest r, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return Error.NotFound();
        }

        if (!await db.ItemCategories.AnyAsync(c => c.Id == r.CategoryId, ct))
        {
            return Error.Validation("ITEM_CATEGORY_NOT_FOUND");
        }

        Apply(item, r);
        item.SetCategory(r.CategoryId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<byte[]> ImportTemplateAsync(ItemImportKind kind, CancellationToken ct)
    {
        var categories = await db.ItemCategories.AsNoTracking().Where(c => c.ImportKind == kind).OrderBy(c => c.SortOrder)
            .Select(c => new { c.Code, c.Name }).ToListAsync(ct);
        return workbook.CreateTemplate(kind, [.. categories.Select(c => (c.Code, c.Name))]);
    }

    private static void Apply(Item item, SaveItemRequest r)
    {
        item.UpdateDetails(r.Name, r.Unit, r.ActiveIngredient, r.Strength, r.Specification, r.Manufacturer, r.Country,
            r.RegistrationNumber, r.BidGroup, r.IsActive);
        item.SetHisKey(r.HisKey);
    }

    private Task<bool> DuplicateAsync(Guid? id, SaveItemRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim();
        return db.Items.AnyAsync(i => i.Id != id && i.Code == code && i.BidDecisionId == null && i.SupplierId == r.SupplierId, ct);
    }

    private IQueryable<ItemDto> Project(IQueryable<Item> query) =>
        query.Select(i => new ItemDto(
            i.Id, i.HisKey, i.Code, i.Name, i.ActiveIngredient, i.Strength, i.Specification, i.Unit, i.Manufacturer, i.Country,
            i.RegistrationNumber, i.BidGroup, i.CategoryId,
            db.ItemCategories.Where(c => c.Id == i.CategoryId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
            i.SupplierId,
            db.Suppliers.Where(s => s.Id == i.SupplierId).Select(s => s.Name).FirstOrDefault(),
            i.BidDecisionId,
            db.BidDecisions.Where(b => b.Id == i.BidDecisionId).Select(b => b.Number).FirstOrDefault(),
            i.WinningUnitPrice, i.Source, i.IsActive));
}

/// <summary>Context of an Excel import: bid decision (supplier per row) or a contract (supplier fixed).</summary>
public sealed record ItemImportContext(ItemImportKind Kind, Guid DefaultCategoryId, bool RequireSupplier, Guid? BidDecisionId, Guid? FixedSupplierId);

/// <summary>Validates workbook rows: required cells, Vietnamese number formats, categories, suppliers, duplicates.</summary>
public sealed partial class ItemImportValidator(IAppDbContext db)
{
    public async Task<(ImportPreviewDto Preview, List<ParsedItemRow> Valid)> ValidateAsync(
        IReadOnlyList<ItemSheetRow> rows, ItemImportContext context, CancellationToken ct)
    {
        var categories = await db.ItemCategories.AsNoTracking().Select(c => new { c.Id, c.Code, c.ImportKind }).ToListAsync(ct);
        var taxCodes = rows.Select(r => Normalize(r.Values.GetValueOrDefault(ItemImportColumns.SupplierTaxCode))).Where(t => t is not null).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => taxCodes.Contains(s.TaxCode)).Select(s => new { s.Id, s.TaxCode, s.Name }).ToListAsync(ct);
        var existingCodes = context.BidDecisionId is { } bidId
            ? await db.Items.AsNoTracking().Where(i => i.BidDecisionId == bidId).Select(i => new { i.Code, i.SupplierId }).ToListAsync(ct)
            : [];

        var preview = new List<ImportRowDto>();
        var valid = new List<ParsedItemRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = ItemImportColumns.For(context.Kind);

        foreach (var row in rows)
        {
            var errors = new List<ImportCellError>();
            string? Get(string key) => row.Values.GetValueOrDefault(key);

            foreach (var column in columns.Where(c => c.Required && (c.Key != ItemImportColumns.SupplierTaxCode || context.RequireSupplier)))
            {
                if (string.IsNullOrWhiteSpace(Get(column.Key)))
                {
                    errors.Add(new(column.Key, $"Thiếu \"{column.Header.Replace(" (*)", string.Empty, StringComparison.Ordinal)}\""));
                }
            }

            var quantity = ParseNumber(Get(ItemImportColumns.Quantity), ItemImportColumns.Quantity, "Số lượng", errors);
            var unitPrice = ParseNumber(Get(ItemImportColumns.UnitPrice), ItemImportColumns.UnitPrice, "Đơn giá", errors);
            var vat = ParseNumber(Get(ItemImportColumns.VatRate), ItemImportColumns.VatRate, "Thuế suất", errors);
            if (quantity is <= 0)
            {
                errors.Add(new(ItemImportColumns.Quantity, "Số lượng phải lớn hơn 0"));
            }

            if (unitPrice is < 0)
            {
                errors.Add(new(ItemImportColumns.UnitPrice, "Đơn giá không được âm"));
            }

            if (vat is < 0 or > 100)
            {
                errors.Add(new(ItemImportColumns.VatRate, "Thuế suất từ 0 đến 100"));
            }

            var categoryId = context.DefaultCategoryId;
            var categoryCode = Get(ItemImportColumns.Category)?.ToUpperInvariant();
            if (categoryCode is not null)
            {
                var category = categories.FirstOrDefault(c => c.Code == categoryCode);
                if (category is null)
                {
                    errors.Add(new(ItemImportColumns.Category, $"Mã hạng mục \"{categoryCode}\" không tồn tại"));
                }
                else if (category.ImportKind != context.Kind)
                {
                    errors.Add(new(ItemImportColumns.Category, "Hạng mục không thuộc biểu mẫu này"));
                }
                else
                {
                    categoryId = category.Id;
                }
            }

            Guid? supplierId = context.FixedSupplierId;
            string? supplierName = null;
            var taxCode = Normalize(Get(ItemImportColumns.SupplierTaxCode));
            if (context.FixedSupplierId is null && taxCode is not null)
            {
                if (!TaxCodePattern().IsMatch(taxCode))
                {
                    errors.Add(new(ItemImportColumns.SupplierTaxCode, "MST không đúng định dạng"));
                }
                else
                {
                    var supplier = suppliers.FirstOrDefault(s => s.TaxCode == taxCode);
                    if (supplier is null)
                    {
                        errors.Add(new(ItemImportColumns.SupplierTaxCode, $"Chưa có NCC mã số thuế {taxCode} trong danh mục"));
                    }
                    else
                    {
                        supplierId = supplier.Id;
                        supplierName = supplier.Name;
                    }
                }
            }

            var code = Get(ItemImportColumns.Code);
            if (code is not null && !seen.Add($"{taxCode}|{code}"))
            {
                errors.Add(new(ItemImportColumns.Code, "Trùng mã hàng của cùng NCC trong tệp"));
            }

            var hisKey = Get(ItemImportColumns.HisKey);
            if (hisKey is not null && !seenHis.Add(hisKey))
            {
                errors.Add(new(ItemImportColumns.HisKey, "Trùng mã key HIS trong tệp"));
            }

            var isUpdate = code is not null && existingCodes.Any(e => e.Code == code && e.SupplierId == supplierId);
            preview.Add(new ImportRowDto(row.RowNumber, hisKey, code, Get(ItemImportColumns.Name), Get(ItemImportColumns.Unit), taxCode, supplierName,
                categoryCode, quantity, unitPrice, vat, isUpdate, errors));

            if (errors.Count == 0)
            {
                valid.Add(new ParsedItemRow(row.RowNumber, hisKey, code!, Get(ItemImportColumns.Name)!, Get(ItemImportColumns.ActiveIngredient),
                    Get(ItemImportColumns.Strength), Get(ItemImportColumns.Specification), Get(ItemImportColumns.Unit)!,
                    Get(ItemImportColumns.Manufacturer), Get(ItemImportColumns.Country), Get(ItemImportColumns.RegistrationNumber),
                    Get(ItemImportColumns.BidGroup), categoryId, supplierId, quantity!.Value, unitPrice!.Value,
                    vat ?? (context.Kind == ItemImportKind.Medical ? 5 : 10)));
            }
        }

        var errorRows = preview.Count(p => p.Errors.Count > 0);
        return (new ImportPreviewDto(preview.Count, preview.Count - errorRows, errorRows, preview), valid);
    }

    /// <summary>Parses "1.234.567", "1234567", "12,5", "1.234,5" or "1234.5" (cells read as numbers come invariant).</summary>
    public static bool TryParseNumber(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("₫", string.Empty, StringComparison.Ordinal).Replace("đ", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (s.Contains('.', StringComparison.Ordinal) && s.Contains(',', StringComparison.Ordinal))
        {
            s = s.LastIndexOf(',') > s.LastIndexOf('.')
                ? s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : s.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (s.Contains(',', StringComparison.Ordinal))
        {
            s = ThousandsComma().IsMatch(s) ? s.Replace(",", string.Empty, StringComparison.Ordinal) : s.Replace(',', '.');
        }
        else if (ThousandsDot().IsMatch(s))
        {
            s = s.Replace(".", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static decimal? ParseNumber(string? text, string column, string label, List<ImportCellError> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (TryParseNumber(text, out var value))
        {
            return value;
        }

        errors.Add(new(column, $"{label} \"{text}\" không phải là số"));
        return null;
    }

    private static string? Normalize(string? taxCode) =>
        string.IsNullOrWhiteSpace(taxCode) ? null : taxCode.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

    [GeneratedRegex(@"^\d{10}(-\d{3})?$")]
    private static partial Regex TaxCodePattern();

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+$")]
    private static partial Regex ThousandsDot();

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3}){2,}$")]
    private static partial Regex ThousandsComma();
}
