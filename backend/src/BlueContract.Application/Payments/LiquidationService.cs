using BlueContract.Application.Common;
using BlueContract.Application.Proposals;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Receipts;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Payments;

/// <summary>
/// Biên bản thanh lý (spec §6.5): tổng hợp các 08a đã ký → đối chiếu Giá trị HĐ / thực hiện / đã thanh toán / còn lại → đánh
/// dấu khoản chưa thanh toán là "đã thanh toán" (chứng từ) để chốt công nợ → Rà soát → NCC + Giám đốc → Văn thư →
/// hợp đồng Đã thanh lý.
/// </summary>
public sealed class LiquidationService(
    IAppDbContext db,
    DocumentNumbers numbers,
    PaymentDocumentData documentData,
    PaymentDocuments documents,
    IFileStorage storage,
    IAuditLogger audit,
    ICurrentUser current,
    IClock clock)
{
    public const string OwnerType = "Liquidation";

    public Task<PagedResult<LiquidationListItemDto>> ListAsync(PageQuery q, LiquidationFilter f, CancellationToken ct)
    {
        var query = db.LiquidationRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(l => EF.Functions.ILike(l.Number, p) || db.Contracts.Any(c => c.Id == l.ContractId && EF.Functions.ILike(c.Number, p)));
        }

        query = f.Status is { } status ? query.Where(l => l.Status == status) : query;
        query = f.ContractId is { } contract ? query.Where(l => l.ContractId == contract) : query;
        query = f.SupplierId is { } supplier ? query.Where(l => l.SupplierId == supplier) : query;
        return query.OrderByDescending(l => l.CreatedAt)
            .Select(l => new LiquidationListItemDto(
                l.Id, l.Number, l.ContractId,
                db.Contracts.Where(c => c.Id == l.ContractId).Select(c => c.Number).FirstOrDefault() ?? string.Empty,
                l.SupplierId,
                db.Suppliers.Where(s => s.Id == l.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                l.ContractValue, l.ExecutedValue, l.PaidAmount, l.Settlements.Sum(s => s.Amount),
                l.ExecutedValue - l.PaidAmount - l.Settlements.Sum(s => s.Amount), l.Status, l.CreatedAt))
            .ToPagedAsync(q, ct);
    }

    /// <summary>Bảng đối chiếu trước khi lập biên bản thanh lý (và lý do chưa lập được).</summary>
    public async Task<Result<LiquidationPreviewDto>> PreviewAsync(Guid contractId, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return Error.NotFound();
        }

        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == contract.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var valuations = await ValuationDraftsAsync(contractId, ct);
        var existing = await db.LiquidationRecords.AsNoTracking()
            .Where(l => l.ContractId == contractId && l.Status != PaymentRecordStatus.Cancelled).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
        var warnings = new List<string>();
        var unaccepted = await db.WarehouseReceipts.CountAsync(r => r.ContractId == contractId && r.Status == ReceiptStatus.Completed && !r.IsIncludedInAcceptance, ct);
        if (unaccepted > 0)
        {
            warnings.Add($"Còn {unaccepted} phiếu nhập kho đã ký chưa nghiệm thu — giá trị của các phiếu này không được tính.");
        }

        var unvalued = await db.AcceptanceRecords.CountAsync(a => a.ContractId == contractId && a.Status == PaymentRecordStatus.Completed && a.ValuationId == null, ct);
        if (unvalued > 0)
        {
            warnings.Add($"Còn {unvalued} biên bản nghiệm thu đã ký chưa lập biên bản xác định giá trị (08a).");
        }

        var pendingReceipts = await db.WarehouseReceipts.CountAsync(r => r.ContractId == contractId
            && (r.Status == ReceiptStatus.Signing || r.Status == ReceiptStatus.Returned || r.Status == ReceiptStatus.Draft), ct);
        if (pendingReceipts > 0)
        {
            warnings.Add($"Còn {pendingReceipts} phiếu nhập kho đang ký hoặc chưa gửi ký.");
        }

        var inProgress = await db.ValuationRecords.AnyAsync(v => v.ContractId == contractId
            && (v.Status == PaymentRecordStatus.Draft || v.Status == PaymentRecordStatus.Signing || v.Status == PaymentRecordStatus.Returned), ct);
        var blocking = contract.Status == ContractStatus.Liquidated ? "CONTRACT_ALREADY_LIQUIDATED"
            : contract.Status != ContractStatus.Effective ? "CONTRACT_NOT_EFFECTIVE"
            : existing is not null ? "LIQUIDATION_EXISTS"
            : inProgress ? "LIQUIDATION_VALUATION_IN_PROGRESS"
            : valuations.Count == 0 ? "LIQUIDATION_VALUATIONS_REQUIRED"
            : null;
        var executed = valuations.Sum(v => v.Amount);
        var paid = valuations.Sum(v => v.PaidAmount);
        return new LiquidationPreviewDto(contract.Id, contract.Number, contract.Name, supplier ?? string.Empty, contract.TotalAmount, executed, paid, executed - paid,
            Math.Max(0, contract.TotalAmount - executed),
            [.. valuations.Select(v => new LiquidationValuationDto(v.ValuationId, v.Number, v.Date, v.Amount, v.PaidAmount, v.Amount - v.PaidAmount, v.PaymentOrderNumbers))],
            warnings, blocking, blocking is null ? null : ErrorMessages.Get(blocking), existing);
    }

    public async Task<Result<LiquidationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var l = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return l is null ? Error.NotFound() : await ToDtoAsync(l, ct);
    }

    public async Task<Result<Guid>> CreateAsync(SaveLiquidationRequest r, CancellationToken ct)
    {
        var preview = await PreviewAsync(r.ContractId, ct);
        if (!preview.IsSuccess)
        {
            return preview.Error!;
        }

        if (preview.Value.BlockingCode is { } code)
        {
            return Error.Conflict(code);
        }

        var contract = await db.Contracts.AsNoTracking().FirstAsync(c => c.Id == r.ContractId, ct);
        var liquidation = new LiquidationRecord(await numbers.NextAsync(DocumentNumbers.Liquidation, ct), contract.Id, contract.SupplierId);
        db.LiquidationRecords.Add(liquidation);
        var applied = await ApplyAsync(liquidation, contract.TotalAmount, r.Notes, ct);
        if (!applied.IsSuccess)
        {
            return applied.Error!;
        }

        audit.Log("LiquidationCreate", nameof(LiquidationRecord), liquidation.Id.ToString(), new { liquidation.Number, ContractNumber = contract.Number, liquidation.RemainingAmount });
        await db.SaveChangesAsync(ct);
        return liquidation.Id;
    }

    /// <summary>Cập nhật ghi chú và lấy lại số liệu (VD sau khi bổ sung UNC).</summary>
    public async Task<Result> UpdateAsync(Guid id, SaveLiquidationRequest r, CancellationToken ct)
    {
        var liquidation = await Query().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidation is null || liquidation.ContractId != r.ContractId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!liquidation.IsEditable)
        {
            return Result.Failure(Error.Conflict("LIQUIDATION_LOCKED"));
        }

        var contractValue = await db.Contracts.Where(c => c.Id == r.ContractId).Select(c => c.TotalAmount).FirstAsync(ct);
        var applied = await ApplyAsync(liquidation, contractValue, r.Notes, ct);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Đánh dấu khoản chưa thanh toán thành "đã thanh toán" kèm chứng từ (lưu người đánh dấu, thời điểm).</summary>
    public async Task<Result<LiquidationDto>> AddSettlementAsync(Guid id, SettlementRequest r, UploadedFile? file, CancellationToken ct)
    {
        var liquidation = await Query().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidation is null)
        {
            return Error.NotFound();
        }

        string? key = null;
        if (file is not null)
        {
            if (FileInspector.Detect(file.Content) is not (FileKind.Pdf or FileKind.Jpeg or FileKind.Png))
            {
                return Error.Validation("PAYMENT_ORDER_FILE_INVALID");
            }

            using var stream = new MemoryStream(file.Content);
            key = (await storage.UploadAsync($"liquidations/{liquidation.Id:N}/voucher-{clock.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(file.FileName)}", stream,
                file.ContentType, ct)).ObjectKey;
        }

        var settlement = liquidation.AddSettlement(r.ValuationId, r.Description, r.Amount, r.VoucherNumber, r.VoucherDate, key, file?.FileName,
            current.UserId!.Value, current.FullName, clock.UtcNow);
        audit.Log("LiquidationSettle", nameof(LiquidationRecord), liquidation.Id.ToString(), new { settlement.Amount, settlement.VoucherNumber, r.ValuationId },
            r.Description);
        var rendered = await RenderAsync(liquidation, "Chốt công nợ", ct);
        if (!rendered.IsSuccess)
        {
            return rendered.Error!;
        }

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(liquidation, ct);
    }

    public async Task<Result<LiquidationDto>> RemoveSettlementAsync(Guid id, Guid settlementId, CancellationToken ct)
    {
        var liquidation = await Query().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidation is null)
        {
            return Error.NotFound();
        }

        liquidation.RemoveSettlement(settlementId);
        var rendered = await RenderAsync(liquidation, "Bỏ khoản chốt công nợ", ct);
        if (!rendered.IsSuccess)
        {
            return rendered.Error!;
        }

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(liquidation, ct);
    }

    public async Task<Result> SubmitAsync(Guid id, CancellationToken ct)
    {
        var liquidation = await Query().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidation?.DocumentId is not { } docId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!liquidation.IsEditable)
        {
            return Result.Failure(Error.Conflict("LIQUIDATION_LOCKED"));
        }

        var submitted = await documents.SubmitAsync(docId, ct);
        if (!submitted.IsSuccess)
        {
            return submitted;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var liquidation = await Query().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidation is null)
        {
            return Result.Failure(Error.NotFound());
        }

        liquidation.Cancel();
        foreach (var valuation in await db.ValuationRecords.Where(v => v.LiquidationId == liquidation.Id).ToListAsync(ct))
        {
            valuation.ReleaseFromLiquidation(liquidation.Id);
        }

        await documents.CancelAsync(liquidation.DocumentId, "Hủy biên bản thanh lý", ct);
        audit.Log("LiquidationCancel", nameof(LiquidationRecord), liquidation.Id.ToString(), new { liquidation.Number });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> ApplyAsync(LiquidationRecord liquidation, decimal contractValue, string? notes, CancellationToken ct)
    {
        var drafts = await ValuationDraftsAsync(liquidation.ContractId, ct);
        liquidation.SetFigures(contractValue, notes, drafts);
        var ids = drafts.Select(d => d.ValuationId).ToList();
        foreach (var valuation in await db.ValuationRecords.Where(v => ids.Contains(v.Id)).ToListAsync(ct))
        {
            valuation.ReserveForLiquidation(liquidation.Id);
        }

        return await RenderAsync(liquidation, liquidation.DocumentId is null ? "Lập biên bản thanh lý" : "Cập nhật biên bản thanh lý", ct);
    }

    private async Task<Result> RenderAsync(LiquidationRecord liquidation, string reason, CancellationToken ct)
    {
        var rendered = await documents.RenderAsync(liquidation.DocumentId, TemplateKind.Liquidation, DocumentType.Liquidation,
            $"Biên bản thanh lý hợp đồng {liquidation.Number}", OwnerType, liquidation.Id, liquidation.SupplierId, liquidation.ContractId,
            await documentData.LiquidationAsync(liquidation, ct), reason, ct);
        if (!rendered.IsSuccess)
        {
            return Result.Failure(rendered.Error!);
        }

        liquidation.AttachDocument(rendered.Value);
        return Result.Success();
    }

    /// <summary>Các 08a đã ký của hợp đồng: giá trị thực hiện (kỳ này) và đã thanh toán (UNC + thu hồi tạm ứng).</summary>
    private async Task<List<LiquidationValuationDraft>> ValuationDraftsAsync(Guid contractId, CancellationToken ct)
    {
        var valuations = await db.ValuationRecords.AsNoTracking().Include(v => v.PaymentOrders)
            .Where(v => v.ContractId == contractId && v.Status == PaymentRecordStatus.Completed)
            .OrderBy(v => v.PaymentSequence).ToListAsync(ct);
        return [.. valuations.Select(v => new LiquidationValuationDraft(v.Id, v.Number,
            DateOnly.FromDateTime((v.CompletedAt ?? v.CreatedAt).ToOffset(TimeSpan.FromHours(7)).DateTime),
            v.CurrentPeriodValue, v.PaidAmount + v.AdvanceRecovered,
            v.PaymentOrders.Count == 0 ? null : string.Join(", ", v.PaymentOrders.Select(p => p.Number))))];
    }

    private IQueryable<LiquidationRecord> Query() =>
        db.LiquidationRecords.Include(l => l.Valuations).Include(l => l.Settlements);

    private async Task<LiquidationDto> ToDtoAsync(LiquidationRecord l, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().Where(c => c.Id == l.ContractId).Select(c => new { c.Number, c.Name }).FirstAsync(ct);
        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == l.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var manage = !current.IsSupplier && current.HasPermission(Permissions.PaymentManage);
        return new LiquidationDto(l.Id, l.Number, l.ContractId, contract.Number, contract.Name, l.SupplierId, supplier ?? string.Empty, l.ContractValue,
            l.ExecutedValue, l.PaidAmount, l.SettledAmount, l.RemainingAmount, l.ReductionAmount, l.Notes, l.Status, l.DocumentId, l.LastReturnReason, l.CompletedAt,
            [.. l.Valuations.OrderBy(v => v.Date).Select(v => new LiquidationValuationDto(v.ValuationId, v.Number, v.Date, v.Amount, v.PaidAmount,
                v.Amount - v.PaidAmount - l.Settlements.Where(s => s.ValuationId == v.ValuationId).Sum(s => s.Amount), v.PaymentOrderNumbers))],
            [.. l.Settlements.OrderBy(s => s.MarkedAt).Select(s => new LiquidationSettlementDto(s.Id, s.ValuationId, s.Description, s.Amount, s.VoucherNumber,
                s.VoucherDate, s.FileKey is not null, s.FileName, s.MarkedByName, s.MarkedAt))],
            CanEdit: manage && l.IsEditable,
            CanSubmit: manage && l.IsEditable && l.DocumentId is not null,
            CanCancel: manage && l.IsEditable);
    }
}
