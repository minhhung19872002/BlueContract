using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Proposals;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Payments;

/// <summary>
/// Biên bản xác định giá trị khối lượng hoàn thành — Mẫu 08a (spec §6.5): chọn BB nghiệm thu đã ký, chưa đưa vào thanh
/// toán → giá trị kỳ này, lũy kế đến kỳ trước / kỳ này, % so với HĐ → sinh Đề nghị thanh toán → ký → Ủy nhiệm chi.
/// </summary>
public sealed class ValuationService(
    IAppDbContext db,
    DocumentNumbers numbers,
    PaymentDocumentData documentData,
    PaymentDocuments documents,
    ContractTracking tracking,
    IFileStorage storage,
    IAuditLogger audit,
    ICurrentUser current,
    IClock clock,
    DataScope dataScope,
    Workflows.WorkflowAccess workflowAccess)
{
    public const string OwnerType = "Valuation";
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    public async Task<PagedResult<ValuationListItemDto>> ListAsync(PageQuery q, ValuationFilter f, CancellationToken ct)
    {
        var query = db.ValuationRecords.AsNoTracking();
        if (await dataScope.ContractIdsAsync(ct) is { } scoped)
        {
            query = query.Where(x => scoped.Contains(x.ContractId));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(v => EF.Functions.ILike(v.Number, p) || EF.Functions.ILike(v.PaymentRequestNumber, p)
                || db.Contracts.Any(c => c.Id == v.ContractId && EF.Functions.ILike(c.Number, p)));
        }

        query = f.Status is { } status ? query.Where(v => v.Status == status) : query;
        query = f.PaymentStatus is { } paid ? query.Where(v => v.PaymentStatus == paid && v.Status != PaymentRecordStatus.Cancelled) : query;
        query = f.ContractId is { } contract ? query.Where(v => v.ContractId == contract) : query;
        query = f.SupplierId is { } supplier ? query.Where(v => v.SupplierId == supplier) : query;

        return await query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id)
            .Select(v => new ValuationListItemDto(
                v.Id, v.Number, v.PaymentRequestNumber, v.ContractId,
                db.Contracts.Where(c => c.Id == v.ContractId).Select(c => c.Number).FirstOrDefault() ?? string.Empty,
                v.SupplierId,
                db.Suppliers.Where(s => s.Id == v.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                v.PaymentSequence, v.PeriodText, v.CurrentPeriodValue, v.PreviousCumulative + v.CurrentPeriodValue,
                v.ContractValue == 0 ? 0 : Math.Round((v.PreviousCumulative + v.CurrentPeriodValue) * 100 / v.ContractValue, 2),
                v.CurrentPeriodValue - v.AdvanceRecovered, v.PaymentOrders.Sum(p => p.Amount), v.PaymentStatus, v.Status, v.PaymentRequestStatus, v.CreatedAt))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<ValuationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var v = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return v is null || !await VisibleAsync(v.ContractId, v.DocumentId, ct) ? Error.NotFound() : await ToDtoAsync(v, ct);
    }

    public async Task<Result<ValuationDto>> CreateAsync(SaveValuationRequest r, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == r.ContractId, ct);
        if (contract is null)
        {
            return Error.Validation("CONTRACT_NOT_FOUND");
        }

        if (contract.Status != ContractStatus.Effective)
        {
            return Error.Validation("CONTRACT_NOT_EFFECTIVE");
        }

        // Max + 1 (not Count + 1): a cancelled 08a in the middle would otherwise hand out a number still in use. The unique
        // index (contract, sequence) of active records settles two simultaneous creations.
        var sequence = (await db.ValuationRecords.Where(v => v.ContractId == contract.Id && v.Status != PaymentRecordStatus.Cancelled)
            .MaxAsync(v => (int?)v.PaymentSequence, ct) ?? 0) + 1;
        var valuation = new ValuationRecord(await numbers.NextAsync(DocumentNumbers.Valuation, ct), await numbers.NextAsync(DocumentNumbers.PaymentRequest, ct),
            contract.Id, contract.SupplierId, sequence, string.IsNullOrWhiteSpace(r.PeriodText) ? $"Lần {sequence}" : r.PeriodText.Trim());
        db.ValuationRecords.Add(valuation);
        var applied = await ApplyAsync(valuation, contract, r, ct);
        if (!applied.IsSuccess)
        {
            return applied.Error!;
        }

        audit.Log("ValuationCreate", nameof(ValuationRecord), valuation.Id.ToString(), new { valuation.Number, ContractNumber = contract.Number, valuation.RequestedAmount });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(valuation, ct);
    }

    public async Task<Result> UpdateAsync(Guid id, SaveValuationRequest r, CancellationToken ct)
    {
        var valuation = await Query().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (valuation is null || valuation.ContractId != r.ContractId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!valuation.IsEditable)
        {
            return Result.Failure(Error.Conflict("VALUATION_LOCKED"));
        }

        var contract = await db.Contracts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == r.ContractId, ct);
        var applied = await ApplyAsync(valuation, contract, r, ct);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Gửi ký 08a (Rà soát → NCC → Giám đốc → Văn thư) và Đề nghị thanh toán (NCC → Giám đốc).</summary>
    public async Task<Result> SubmitAsync(Guid id, CancellationToken ct)
    {
        var valuation = await Query().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (valuation?.DocumentId is not { } docId || valuation.PaymentRequestDocumentId is not { } requestId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!valuation.IsEditable)
        {
            return Result.Failure(Error.Conflict("VALUATION_LOCKED"));
        }

        var submitted = await documents.SubmitAsync(docId, ct);
        if (!submitted.IsSuccess)
        {
            return submitted;
        }

        if (valuation.PaymentRequestStatus is PaymentRecordStatus.Draft or PaymentRecordStatus.Returned)
        {
            var request = await documents.SubmitAsync(requestId, ct);
            if (!request.IsSuccess)
            {
                return request;
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var valuation = await Query().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (valuation is null)
        {
            return Result.Failure(Error.NotFound());
        }

        valuation.Cancel();
        await ReleaseAcceptancesAsync(valuation, null, ct);
        await documents.CancelAsync(valuation.DocumentId, "Hủy biên bản xác định giá trị", ct);
        await documents.CancelAsync(valuation.PaymentRequestDocumentId, "Hủy đề nghị thanh toán", ct);
        audit.Log("ValuationCancel", nameof(ValuationRecord), valuation.Id.ToString(), new { valuation.Number });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Đính kèm Ủy nhiệm chi (số, ngày, số tiền, file) — được bổ sung cả sau khi ký (spec §6.5).</summary>
    public async Task<Result<ValuationDto>> AddPaymentOrderAsync(Guid id, PaymentOrderRequest r, UploadedFile? file, CancellationToken ct)
    {
        var valuation = await Query().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (valuation is null)
        {
            return Error.NotFound();
        }

        string? key = null;
        if (file is not null)
        {
            var kind = FileInspector.Detect(file.Content);
            if (kind is not (FileKind.Pdf or FileKind.Jpeg or FileKind.Png))
            {
                return Error.Validation("PAYMENT_ORDER_FILE_INVALID");
            }

            var stored = FileInspector.Storage(kind);
            using var stream = new MemoryStream(file.Content);
            key = (await storage.UploadAsync($"valuations/{valuation.Id:N}/unc-{clock.UtcNow:yyyyMMddHHmmss}{stored.Extension}", stream,
                stored.MediaType, ct)).ObjectKey;
        }

        var order = valuation.AddPaymentOrder(r.Number, r.Date, r.Amount, key, file?.FileName, current.FullName, clock.UtcNow);
        audit.Log("PaymentOrderAdd", nameof(ValuationRecord), valuation.Id.ToString(), new { order.Number, order.Amount, valuation.PaymentStatus });
        tracking.Defer(valuation.Items.Select(i => i.ContractItemId));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(valuation, ct);
    }

    public async Task<Result<ValuationDto>> RemovePaymentOrderAsync(Guid id, Guid orderId, CancellationToken ct)
    {
        var valuation = await Query().FirstOrDefaultAsync(v => v.Id == id, ct);
        if (valuation is null)
        {
            return Error.NotFound();
        }

        valuation.RemovePaymentOrder(orderId);
        audit.Log("PaymentOrderRemove", nameof(ValuationRecord), valuation.Id.ToString(), new { orderId });
        tracking.Defer(valuation.Items.Select(i => i.ContractItemId));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(valuation, ct);
    }

    public async Task<Result<FileLinkDto>> PaymentOrderFileAsync(Guid id, Guid orderId, CancellationToken ct)
    {
        var order = await db.ValuationRecords.AsNoTracking().Where(v => v.Id == id).SelectMany(v => v.PaymentOrders).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order?.FileKey is null)
        {
            return Error.NotFound();
        }

        return new FileLinkDto(await storage.GetPresignedDownloadUrlAsync(order.FileKey, LinkLifetime, ct), order.FileName ?? "uy-nhiem-chi", clock.UtcNow.Add(LinkLifetime));
    }

    /// <summary>
    /// Builds the 08a from completed, not yet used acceptances: kỳ này = Σ nghiệm thu; lũy kế đến kỳ trước = Σ kỳ này của
    /// các 08a trước (chưa hủy) của hợp đồng; khối lượng theo mặt hàng tương tự.
    /// </summary>
    private async Task<Result> ApplyAsync(ValuationRecord valuation, Contract contract, SaveValuationRequest r, CancellationToken ct)
    {
        var ids = r.AcceptanceIds.Distinct().ToList();
        var acceptances = await db.AcceptanceRecords.Include(a => a.Items).Where(a => ids.Contains(a.Id) && a.ContractId == contract.Id).ToListAsync(ct);
        if (acceptances.Count != ids.Count || acceptances.Any(a => a.Status != PaymentRecordStatus.Completed))
        {
            return Result.Failure(Error.Validation("VALUATION_ACCEPTANCE_NOT_ELIGIBLE"));
        }

        if (acceptances.Any(a => a.ValuationId is not null && a.ValuationId != valuation.Id))
        {
            return Result.Failure(Error.Conflict("ACCEPTANCE_ALREADY_IN_VALUATION"));
        }

        var previous = await db.ValuationRecords.AsNoTracking().Include(v => v.Items).Include(v => v.PaymentOrders)
            .Where(v => v.ContractId == contract.Id && v.Id != valuation.Id && v.Status != PaymentRecordStatus.Cancelled && v.PaymentSequence < valuation.PaymentSequence)
            .ToListAsync(ct);
        var previousQuantity = previous.SelectMany(v => v.Items).GroupBy(i => i.ContractItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.CurrentQuantity));
        var current = acceptances.SelectMany(a => a.Items).GroupBy(i => i.ContractItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var items = contract.Items
            .Where(i => current.ContainsKey(i.Id) || previousQuantity.ContainsKey(i.Id))
            .Select(i => new ValuationItemDraft(i.Id, i.ItemName, i.Unit, i.Quantity, i.UnitPrice, previousQuantity.GetValueOrDefault(i.Id), current.GetValueOrDefault(i.Id)))
            .ToList();

        await ReleaseAcceptancesAsync(valuation, ids, ct);
        valuation.SetContent(string.IsNullOrWhiteSpace(r.PeriodText) ? valuation.PeriodText : r.PeriodText.Trim(),
            new ValuationFigures(contract.TotalAmount, r.AdvanceAmount, previous.Sum(v => v.CurrentPeriodValue), r.AdvanceRecovered),
            [.. acceptances.OrderBy(a => a.PeriodFrom).Select(a => new ValuationAcceptanceDraft(a.Id, a.Number, a.PeriodText, a.TotalAmount))], items);
        foreach (var acceptance in acceptances)
        {
            acceptance.ReserveForValuation(valuation.Id);
        }

        var paidBefore = previous.Sum(v => v.PaymentOrders.Sum(p => p.Amount)) + previous.Sum(v => v.AdvanceRecovered);
        var document = await documents.RenderAsync(valuation.DocumentId, TemplateKind.Valuation, DocumentType.Valuation,
            $"Biên bản xác định giá trị khối lượng hoàn thành (08a) {valuation.Number}", OwnerType, valuation.Id, valuation.SupplierId, valuation.ContractId,
            await documentData.ValuationAsync(valuation, ct), valuation.DocumentId is null ? "Lập biên bản 08a" : "Cập nhật biên bản 08a", ct);
        if (!document.IsSuccess)
        {
            return Result.Failure(document.Error!);
        }

        var request = await documents.RenderAsync(valuation.PaymentRequestDocumentId, TemplateKind.PaymentRequest, DocumentType.PaymentRequest,
            $"Đề nghị thanh toán {valuation.PaymentRequestNumber}", OwnerType, valuation.Id, valuation.SupplierId, valuation.ContractId,
            await documentData.PaymentRequestAsync(valuation, paidBefore, ct), "Đề nghị thanh toán kèm biên bản 08a", ct);
        if (!request.IsSuccess)
        {
            return Result.Failure(request.Error!);
        }

        valuation.AttachDocuments(document.Value, request.Value);
        return Result.Success();
    }

    private async Task ReleaseAcceptancesAsync(ValuationRecord valuation, IReadOnlyCollection<Guid>? keep, CancellationToken ct)
    {
        var held = await db.AcceptanceRecords.Where(a => a.ValuationId == valuation.Id).ToListAsync(ct);
        foreach (var acceptance in held.Where(a => keep is null || !keep.Contains(a.Id)))
        {
            acceptance.ReleaseFromValuation(valuation.Id);
        }
    }

    private IQueryable<ValuationRecord> Query() =>
        db.ValuationRecords.Include(v => v.Acceptances).Include(v => v.Items).Include(v => v.PaymentOrders);

    private async Task<ValuationDto> ToDtoAsync(ValuationRecord v, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().Where(c => c.Id == v.ContractId).Select(c => new { c.Number, c.Name }).FirstAsync(ct);
        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == v.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var manage = !current.IsSupplier && current.HasPermission(Permissions.PaymentManage);
        return new ValuationDto(v.Id, v.Number, v.PaymentRequestNumber, v.ContractId, contract.Number, contract.Name, v.SupplierId, supplier ?? string.Empty,
            v.PaymentSequence, v.PeriodText, v.ContractValue, v.AdvanceAmount, v.PreviousCumulative, v.CurrentPeriodValue, v.CumulativeToDate, v.CompletionPercent,
            v.AdvanceRecovered, v.RequestedAmount, v.PaidAmount, v.PaymentStatus, v.SettledByLiquidationId is not null, v.Status, v.PaymentRequestStatus,
            v.DocumentId, v.PaymentRequestDocumentId, v.LastReturnReason, v.CompletedAt,
            [.. v.Acceptances.OrderBy(a => a.Number).Select(a => new ValuationAcceptanceDto(a.AcceptanceId, a.Number, a.PeriodText, a.Amount))],
            [.. v.Items.OrderBy(i => i.ItemName).Select(i => new ValuationItemDto(i.ContractItemId, i.ItemName, i.Unit, i.ContractQuantity, i.UnitPrice,
                i.PreviousQuantity, i.CurrentQuantity, i.CumulativeQuantity, i.ContractAmount, i.PreviousAmount, i.CurrentAmount, i.CumulativeAmount))],
            [.. v.PaymentOrders.OrderBy(p => p.Date).Select(p => new PaymentOrderDto(p.Id, p.Number, p.Date, p.Amount, p.FileKey is not null, p.FileName,
                p.AddedByName, p.AddedAt))],
            CanEdit: manage && v.IsEditable,
            CanSubmit: manage && v.IsEditable && v.DocumentId is not null,
            CanCancel: manage && v.IsEditable,
            CanManagePaymentOrders: manage && v.Status != PaymentRecordStatus.Cancelled && v.SettledByLiquidationId is null && v.LiquidationId is null);
    }

    /// <summary>Data scope (spec §4.2): out-of-scope records look missing, unless the user takes part in their signing.</summary>
    private async Task<bool> VisibleAsync(Guid contractId, Guid? documentId, CancellationToken ct) =>
        await dataScope.CanSeeContractAsync(contractId, ct) || (documentId is { } doc && await workflowAccess.IsParticipantAsync(doc, ct));
}
