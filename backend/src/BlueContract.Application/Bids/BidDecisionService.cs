using System.Linq.Expressions;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Integration;
using BlueContract.Domain.Bids;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Bids;

public sealed record BidDecisionListItemDto(
    Guid Id,
    string Number,
    DateOnly SignedDate,
    string PackageName,
    string Summary,
    Guid CategoryId,
    string CategoryName,
    BidDecisionStatus Status,
    int SupplierCount,
    int ItemCount,
    decimal TotalValue,
    int ContractCount);

public sealed record BidSupplierDto(Guid SupplierId, string TaxCode, string Name, decimal WinningValue, int ItemCount, decimal ItemsValue, decimal ContractedValue);

public sealed record BidItemDto(
    Guid ItemId,
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
    Guid SupplierId,
    string SupplierName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    decimal ContractedQuantity,
    decimal RemainingQuantity,
    ItemSource Source);

public sealed record BidContractDto(Guid Id, string Number, string Name, string SupplierName, ContractStatus Status, decimal TotalAmount);

public sealed record BidDecisionDto(
    Guid Id,
    string Number,
    DateOnly SignedDate,
    string Summary,
    string PackageName,
    string? SelectionMethod,
    string? FundingSource,
    string? ExecutionPeriod,
    Guid CategoryId,
    string CategoryName,
    ItemImportKind ImportKind,
    Guid? FacilityId,
    DateOnly? ValidUntil,
    string? AttachmentName,
    string? AttachmentSha256,
    BidDecisionStatus Status,
    IReadOnlyList<BidSupplierDto> Suppliers,
    IReadOnlyList<BidContractDto> Contracts,
    string HisName);

public sealed record SaveBidDecisionRequest(
    string Number,
    DateOnly SignedDate,
    string Summary,
    string PackageName,
    string? SelectionMethod,
    string? FundingSource,
    string? ExecutionPeriod,
    Guid CategoryId,
    Guid? FacilityId,
    DateOnly? ValidUntil);

public sealed record BidSupplierRequest(Guid SupplierId, decimal WinningValue);

public sealed record BidItemRequest(
    Guid SupplierId,
    string Code,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    string? HisKey,
    string? ActiveIngredient,
    string? Strength,
    string? Specification,
    string? Manufacturer,
    string? Country,
    string? RegistrationNumber,
    string? BidGroup);

public sealed record BidFilter(int? Year, Guid? SupplierId, Guid? CategoryId, BidDecisionStatus? Status);

public sealed record HisSyncResultDto(int Suppliers, int Created, int Updated, int Unchanged, IReadOnlyList<string> Messages);

internal sealed class SaveBidDecisionRequestValidator : AbstractValidator<SaveBidDecisionRequest>
{
    public SaveBidDecisionRequestValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithName("Số quyết định").MaximumLength(100);
        RuleFor(x => x.Summary).NotEmpty().WithName("Trích yếu").MaximumLength(2000);
        RuleFor(x => x.PackageName).NotEmpty().WithName("Tên gói thầu").MaximumLength(500);
        RuleFor(x => x.CategoryId).NotEmpty().WithName("Hạng mục");
        RuleFor(x => x.SignedDate).NotEmpty().WithName("Ngày ký");
    }
}

internal sealed class BidItemRequestValidator : AbstractValidator<BidItemRequest>
{
    public BidItemRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty().WithName("Nhà cung cấp");
        RuleFor(x => x.Code).NotEmpty().WithName("Mã hàng");
        RuleFor(x => x.Name).NotEmpty().WithName("Tên hàng");
        RuleFor(x => x.Unit).NotEmpty().WithName("Đơn vị tính");
        RuleFor(x => x.Quantity).GreaterThan(0).WithName("Số lượng trúng thầu");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithName("Đơn giá trúng thầu");
    }
}

/// <summary>Quyết định thầu (spec §6.1 B1): header + PDF, winning suppliers, item list from the HIS or Excel.</summary>
public sealed class BidDecisionService(
    IAppDbContext db,
    IHisClient his,
    IItemWorkbook workbook,
    ItemImportValidator importValidator,
    IFileStorage storage,
    IAuditLogger audit)
{
    private static readonly Dictionary<string, Expression<Func<BidDecision, object?>>> Sorts = new()
    {
        ["number"] = x => x.Number,
        ["signedDate"] = x => x.SignedDate,
        ["packageName"] = x => x.PackageName,
    };

    public Task<PagedResult<BidDecisionListItemDto>> ListAsync(PageQuery q, BidFilter f, CancellationToken ct)
    {
        var query = db.BidDecisions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(b => EF.Functions.ILike(b.Number, p) || EF.Functions.ILike(b.PackageName, p) || EF.Functions.ILike(b.Summary, p));
        }

        if (f.Year is { } year)
        {
            var from = new DateOnly(year, 1, 1);
            var to = new DateOnly(year, 12, 31);
            query = query.Where(b => b.SignedDate >= from && b.SignedDate <= to);
        }

        if (f.SupplierId is not null)
        {
            query = query.Where(b => b.Suppliers.Any(s => s.SupplierId == f.SupplierId));
        }

        if (f.CategoryId is not null)
        {
            query = query.Where(b => b.CategoryId == f.CategoryId);
        }

        if (f.Status is not null)
        {
            query = query.Where(b => b.Status == f.Status);
        }

        return query.ApplySort(q, Sorts, "signedDate", defaultDesc: true)
            .Select(b => new BidDecisionListItemDto(
                b.Id, b.Number, b.SignedDate, b.PackageName, b.Summary, b.CategoryId,
                db.ItemCategories.Where(c => c.Id == b.CategoryId).Select(c => c.Name).FirstOrDefault() ?? string.Empty,
                b.Status, b.Suppliers.Count, b.Items.Count, b.Suppliers.Sum(s => s.WinningValue),
                db.Contracts.Count(c => c.BidDecisionId == b.Id && c.Status != ContractStatus.Cancelled)))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<BidDecisionDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var b = await db.BidDecisions.AsNoTracking().Include(x => x.Suppliers).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null)
        {
            return Error.NotFound();
        }

        var category = await db.ItemCategories.AsNoTracking().FirstAsync(c => c.Id == b.CategoryId, ct);
        var supplierIds = b.Suppliers.Select(s => s.SupplierId).ToList();
        var suppliers = await db.Suppliers.AsNoTracking().IgnoreQueryFilters().Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new { s.TaxCode, s.Name }, ct);
        var contracts = await db.Contracts.AsNoTracking().Where(c => c.BidDecisionId == id)
            .OrderBy(c => c.Number)
            .Select(c => new { c.Id, c.Number, c.Name, c.SupplierId, c.Status, c.TotalAmount })
            .ToListAsync(ct);

        var supplierDtos = b.Suppliers.Select(s => new BidSupplierDto(
                s.SupplierId,
                suppliers.GetValueOrDefault(s.SupplierId)?.TaxCode ?? string.Empty,
                suppliers.GetValueOrDefault(s.SupplierId)?.Name ?? string.Empty,
                s.WinningValue,
                b.Items.Count(i => i.SupplierId == s.SupplierId),
                b.Items.Where(i => i.SupplierId == s.SupplierId).Sum(i => i.Amount),
                contracts.Where(c => c.SupplierId == s.SupplierId && c.Status != ContractStatus.Cancelled).Sum(c => c.TotalAmount)))
            .OrderBy(s => s.Name)
            .ToList();
        var contractDtos = contracts.Select(c => new BidContractDto(c.Id, c.Number, c.Name, suppliers.GetValueOrDefault(c.SupplierId)?.Name ?? string.Empty,
            c.Status, c.TotalAmount)).ToList();

        return new BidDecisionDto(b.Id, b.Number, b.SignedDate, b.Summary, b.PackageName, b.SelectionMethod, b.FundingSource, b.ExecutionPeriod,
            b.CategoryId, category.Name, category.ImportKind, b.FacilityId, b.ValidUntil, b.AttachmentName, b.AttachmentSha256, b.Status,
            supplierDtos, contractDtos, his.DisplayName);
    }

    /// <summary>Winning items with the quantity already put into (non-cancelled) contracts.</summary>
    public async Task<Result<List<BidItemDto>>> ItemsAsync(Guid id, Guid? supplierId, CancellationToken ct)
    {
        if (!await db.BidDecisions.AnyAsync(b => b.Id == id, ct))
        {
            return Error.NotFound();
        }

        var rows = await db.BidDecisions.AsNoTracking().Where(b => b.Id == id).SelectMany(b => b.Items)
            .Where(i => supplierId == null || i.SupplierId == supplierId)
            .Join(db.Items.AsNoTracking(), bi => bi.ItemId, it => it.Id, (bi, it) => new { bi, it })
            .Select(x => new
            {
                x.bi,
                x.it,
                SupplierName = db.Suppliers.Where(s => s.Id == x.bi.SupplierId).Select(s => s.Name).FirstOrDefault(),
                Contracted = db.Contracts.Where(c => c.BidDecisionId == id && c.Status != ContractStatus.Cancelled)
                    .SelectMany(c => c.Items).Where(ci => ci.ItemId == x.bi.ItemId).Sum(ci => (decimal?)ci.Quantity) ?? 0,
            })
            .OrderBy(x => x.SupplierName).ThenBy(x => x.it.Code)
            .ToListAsync(ct);

        return rows.Select(x => new BidItemDto(
            x.it.Id, x.it.HisKey, x.it.Code, x.it.Name, x.it.ActiveIngredient, x.it.Strength, x.it.Specification, x.it.Unit, x.it.Manufacturer,
            x.it.Country, x.it.RegistrationNumber, x.it.BidGroup, x.bi.SupplierId, x.SupplierName ?? string.Empty, x.bi.Quantity, x.bi.UnitPrice,
            x.bi.Amount, x.Contracted, x.bi.Quantity - x.Contracted, x.it.Source)).ToList();
    }

    public async Task<Result<Guid>> CreateAsync(SaveBidDecisionRequest r, CancellationToken ct)
    {
        var number = BidDecision.NormalizeNumber(r.Number);
        if (await db.BidDecisions.AnyAsync(b => b.Number == number, ct))
        {
            return Error.Conflict("BID_NUMBER_EXISTS");
        }

        if (!await db.ItemCategories.AnyAsync(c => c.Id == r.CategoryId, ct))
        {
            return Error.Validation("ITEM_CATEGORY_NOT_FOUND");
        }

        var bid = new BidDecision(number, r.SignedDate, r.Summary, r.PackageName, r.CategoryId);
        bid.Update(number, r.SignedDate, r.Summary, r.PackageName, r.SelectionMethod, r.FundingSource, r.ExecutionPeriod, r.CategoryId, r.FacilityId, r.ValidUntil);
        db.BidDecisions.Add(bid);
        await db.SaveChangesAsync(ct);
        return bid.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, SaveBidDecisionRequest r, CancellationToken ct)
    {
        var bid = await db.BidDecisions.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Result.Failure(Error.NotFound());
        }

        var number = BidDecision.NormalizeNumber(r.Number);
        if (await db.BidDecisions.AnyAsync(b => b.Id != id && b.Number == number, ct))
        {
            return Result.Failure(Error.Conflict("BID_NUMBER_EXISTS"));
        }

        bid.Update(number, r.SignedDate, r.Summary, r.PackageName, r.SelectionMethod, r.FundingSource, r.ExecutionPeriod, r.CategoryId, r.FacilityId, r.ValidUntil);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Đính kèm file PDF quyết định.</summary>
    public async Task<Result> AttachAsync(Guid id, UploadedFile file, CancellationToken ct)
    {
        var bid = await db.BidDecisions.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (FileInspector.Detect(file.Content) != FileKind.Pdf)
        {
            return Result.Failure(Error.Validation("DOCUMENT_FILE_TYPE_INVALID"));
        }

        using var stream = new MemoryStream(file.Content);
        var stored = await storage.UploadAsync($"bid-decisions/{bid.Id:N}/{Guid.CreateVersion7():N}.pdf", stream, "application/pdf", ct);
        bid.AttachFile(stored.ObjectKey, file.FileName, stored.Sha256);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<(byte[] Content, string FileName)>> AttachmentAsync(Guid id, CancellationToken ct)
    {
        var bid = await db.BidDecisions.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid?.AttachmentKey is null)
        {
            return Error.NotFound("DOCUMENT_FILE_NOT_FOUND");
        }

        await using var stream = await storage.DownloadAsync(bid.AttachmentKey, ct);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        audit.Log("Download", nameof(BidDecision), id.ToString(), new { bid.AttachmentName });
        await db.SaveChangesAsync(ct);
        return (copy.ToArray(), bid.AttachmentName ?? "quyet-dinh.pdf");
    }

    public async Task<Result> UpsertSupplierAsync(Guid id, BidSupplierRequest r, CancellationToken ct)
    {
        var bid = await db.BidDecisions.Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!await db.Suppliers.AnyAsync(s => s.Id == r.SupplierId, ct))
        {
            return Result.Failure(Error.Validation("SUPPLIER_NOT_FOUND"));
        }

        bid.UpsertSupplier(r.SupplierId, r.WinningValue);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveSupplierAsync(Guid id, Guid supplierId, CancellationToken ct)
    {
        var bid = await db.BidDecisions.Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (await db.Contracts.AnyAsync(c => c.BidDecisionId == id && c.SupplierId == supplierId && c.Status != ContractStatus.Cancelled, ct))
        {
            return Result.Failure(Error.Conflict("BID_SUPPLIER_HAS_CONTRACTS"));
        }

        bid.RemoveSupplier(supplierId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Nhập tay một mặt hàng trúng thầu (danh mục không qua HIS).</summary>
    public async Task<Result<Guid>> AddItemAsync(Guid id, BidItemRequest r, CancellationToken ct)
    {
        var bid = await db.BidDecisions.Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Error.NotFound();
        }

        var row = new ParsedItemRow(0, r.HisKey, r.Code.Trim(), r.Name, r.ActiveIngredient, r.Strength, r.Specification, r.Unit, r.Manufacturer,
            r.Country, r.RegistrationNumber, r.BidGroup, bid.CategoryId, r.SupplierId, r.Quantity, r.UnitPrice, 0);
        var (item, _) = await UpsertItemAsync(bid, row, ItemSource.Manual, ct);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task<Result> RemoveItemAsync(Guid id, Guid itemId, CancellationToken ct)
    {
        var bid = await db.BidDecisions.Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (await db.Contracts.AnyAsync(c => c.BidDecisionId == id && c.Status != ContractStatus.Cancelled && c.Items.Any(i => i.ItemId == itemId), ct))
        {
            return Result.Failure(Error.Conflict("BID_ITEM_IN_CONTRACT"));
        }

        bid.RemoveItem(itemId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Cách A: đồng bộ danh mục từ HIS theo (Số QĐ + MST) cho từng NCC trúng thầu.</summary>
    public async Task<Result<HisSyncResultDto>> SyncFromHisAsync(Guid id, Guid? supplierId, CancellationToken ct)
    {
        var bid = await db.BidDecisions.Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Error.NotFound();
        }

        var targets = bid.Suppliers.Where(s => supplierId is null || s.SupplierId == supplierId).Select(s => s.SupplierId).ToList();
        if (targets.Count == 0)
        {
            return Error.Validation("BID_NO_SUPPLIER");
        }

        var facilityCode = bid.FacilityId is { } fid ? await db.Facilities.Where(f => f.Id == fid).Select(f => f.Code).FirstOrDefaultAsync(ct) : null;
        var categories = await db.ItemCategories.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.Id, ct);
        int created = 0, updated = 0, unchanged = 0;
        var messages = new List<string>();
        foreach (var sid in targets)
        {
            var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == sid, ct);
            IReadOnlyList<HisItem> items;
            try
            {
                items = await his.GetItemsByBidDecisionAsync(bid.Number, supplier.TaxCode, facilityCode, ct);
            }
            catch (HisUnavailableException ex)
            {
                _ = ex;
                return Error.Validation("HIS_UNAVAILABLE");
            }

            if (items.Count == 0)
            {
                messages.Add($"HIS chưa có danh mục của {supplier.Name} cho quyết định {bid.Number}.");
                continue;
            }

            foreach (var h in items)
            {
                var row = new ParsedItemRow(0, h.HisKey, h.Code, h.Name, h.ActiveIngredient, h.Strength, h.Specification, h.Unit, h.Manufacturer,
                    h.Country, h.RegistrationNumber, h.BidGroup,
                    h.CategoryCode is not null && categories.TryGetValue(h.CategoryCode, out var cid) ? cid : bid.CategoryId,
                    sid, h.Quantity, h.UnitPrice, 0);
                var (_, outcome) = await UpsertItemAsync(bid, row, ItemSource.His, ct);
                switch (outcome)
                {
                    case UpsertOutcome.Created:
                        created++;
                        break;
                    case UpsertOutcome.Updated:
                        updated++;
                        break;
                    default:
                        unchanged++;
                        break;
                }
            }

            messages.Add($"{supplier.Name}: {items.Count} mặt hàng từ HIS.");
        }

        audit.Log("HisSync", nameof(BidDecision), bid.Id.ToString(), new { created, updated, unchanged });
        await db.SaveChangesAsync(ct);
        return new HisSyncResultDto(targets.Count, created, updated, unchanged, messages);
    }

    /// <summary>Cách B: kiểm tra tệp Excel — trả về từng dòng kèm lỗi để hiển thị (không ghi dữ liệu).</summary>
    public async Task<Result<ImportPreviewDto>> PreviewImportAsync(Guid id, byte[] xlsx, CancellationToken ct)
    {
        var parsed = await ParseAsync(id, xlsx, ct);
        return parsed.IsSuccess ? parsed.Value.Preview : parsed.Error!;
    }

    /// <summary>Import chỉ thực hiện khi toàn bộ tệp hợp lệ (spec §6.1 B1).</summary>
    public async Task<Result<ImportResultDto>> CommitImportAsync(Guid id, byte[] xlsx, CancellationToken ct)
    {
        var parsed = await ParseAsync(id, xlsx, ct);
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        var (preview, valid, bid) = parsed.Value;
        if (preview.ErrorRows > 0)
        {
            return Error.Validation("IMPORT_HAS_ERRORS");
        }

        int created = 0, updated = 0;
        foreach (var row in valid)
        {
            // Suppliers listed in the file become winners of the decision.
            if (bid.Suppliers.All(s => s.SupplierId != row.SupplierId))
            {
                bid.UpsertSupplier(row.SupplierId!.Value, 0);
            }

            var (_, outcome) = await UpsertItemAsync(bid, row, ItemSource.Import, ct);
            if (outcome == UpsertOutcome.Created)
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        // Winning value defaults to the sum of the supplier's items when not entered.
        foreach (var s in bid.Suppliers.Where(s => s.WinningValue == 0))
        {
            bid.UpsertSupplier(s.SupplierId, bid.Items.Where(i => i.SupplierId == s.SupplierId).Sum(i => i.Amount));
        }

        audit.Log("ItemImport", nameof(BidDecision), bid.Id.ToString(), new { created, updated, rows = valid.Count });
        await db.SaveChangesAsync(ct);
        return new ImportResultDto(created, updated);
    }

    public async Task<Result> ActivateAsync(Guid id, CancellationToken ct)
    {
        var bid = await db.BidDecisions.Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Result.Failure(Error.NotFound());
        }

        bid.Activate();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> CloseAsync(Guid id, CancellationToken ct)
    {
        var bid = await db.BidDecisions.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Result.Failure(Error.NotFound());
        }

        bid.Close();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<(ImportPreviewDto Preview, List<ParsedItemRow> Valid, BidDecision Bid)>> ParseAsync(Guid id, byte[] xlsx, CancellationToken ct)
    {
        var bid = await db.BidDecisions.Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bid is null)
        {
            return Error.NotFound();
        }

        if (FileInspector.Detect(xlsx) != FileKind.Xlsx)
        {
            return Error.Validation("IMPORT_NOT_XLSX");
        }

        var category = await db.ItemCategories.AsNoTracking().FirstAsync(c => c.Id == bid.CategoryId, ct);
        IReadOnlyList<ItemSheetRow> rows;
        try
        {
            rows = workbook.Read(xlsx, category.ImportKind);
        }
        catch (InvalidDataException)
        {
            return Error.Validation("IMPORT_NOT_XLSX");
        }

        if (rows.Count == 0)
        {
            return Error.Validation("IMPORT_EMPTY");
        }

        var (preview, valid) = await importValidator.ValidateAsync(rows,
            new ItemImportContext(category.ImportKind, bid.CategoryId, RequireSupplier: true, bid.Id, FixedSupplierId: null), ct);
        return (preview, valid, bid);
    }

    private enum UpsertOutcome
    {
        Created,
        Updated,
        Unchanged,
    }

    /// <summary>Finds the item of this bid + supplier by HIS key or code, creates it otherwise, and sets its winning line.</summary>
    private async Task<(Item Item, UpsertOutcome Outcome)> UpsertItemAsync(BidDecision bid, ParsedItemRow row, ItemSource source, CancellationToken ct)
    {
        var supplierId = row.SupplierId!.Value;
        var item = await db.Items.FirstOrDefaultAsync(i => i.BidDecisionId == bid.Id && i.SupplierId == supplierId &&
            ((row.HisKey != null && i.HisKey == row.HisKey) || i.Code == row.Code), ct);
        var outcome = UpsertOutcome.Updated;
        if (item is null)
        {
            item = new Item(row.Code, row.Name, row.Unit, row.CategoryId, source);
            db.Items.Add(item);
            outcome = UpsertOutcome.Created;
        }
        else if (item.Name == row.Name && item.Unit == row.Unit && item.WinningUnitPrice == row.UnitPrice &&
                 bid.Items.Any(i => i.ItemId == item.Id && i.Quantity == row.Quantity))
        {
            outcome = UpsertOutcome.Unchanged;
        }

        item.UpdateDetails(row.Name, row.Unit, row.ActiveIngredient, row.Strength, row.Specification, row.Manufacturer, row.Country,
            row.RegistrationNumber, row.BidGroup, isActive: true);
        item.SetCategory(row.CategoryId);
        item.SetFacility(bid.FacilityId);
        if (row.HisKey is not null)
        {
            if (source == ItemSource.His)
            {
                item.MarkSyncedFromHis(row.HisKey);
            }
            else
            {
                item.SetHisKey(row.HisKey);
            }
        }

        item.AssignToBid(bid.Id, supplierId, row.UnitPrice);
        bid.UpsertItem(item.Id, supplierId, row.Quantity, row.UnitPrice);
        return (item, outcome);
    }
}
