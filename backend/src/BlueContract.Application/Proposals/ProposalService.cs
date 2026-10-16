using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Integration;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Workflows;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Proposals;

public sealed record ProposalListItemDto(
    Guid Id,
    string Number,
    string DepartmentName,
    ProcurementDomain Domain,
    string PeriodText,
    ProposalStatus Status,
    int LineCount,
    decimal TotalQuantity,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    string? SummaryNumber);

public sealed record ProposalFilter(ProcurementDomain? Domain, ProposalStatus? Status, Guid? DepartmentId, Guid? ContractId = null);

public sealed record ProposalLineDto(
    Guid Id,
    Guid ContractId,
    string ContractNumber,
    Guid ContractItemId,
    string ItemCode,
    string ItemName,
    string Unit,
    string? HisKey,
    decimal OpeningStock,
    decimal Received,
    decimal Issued,
    decimal CurrentStock,
    decimal ContractRemaining,
    decimal ProposedQuantity,
    string? Note);

public sealed record ProposalDto(
    Guid Id,
    string Number,
    Guid DepartmentId,
    string DepartmentName,
    Guid WarehouseId,
    string WarehouseName,
    ProcurementDomain Domain,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string PeriodText,
    string? Note,
    ProposalStatus Status,
    string? CreatedByName,
    Guid? DocumentId,
    Guid? SummaryId,
    IReadOnlyList<ProposalLineDto> Lines,
    bool CanEdit,
    bool CanSubmit,
    bool CanCancel);

/// <summary>A contract item that can be proposed, with its HIS stock movement for the period.</summary>
public sealed record ProposalCandidateDto(
    Guid ContractId,
    string ContractNumber,
    string SupplierName,
    Guid ContractItemId,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string Unit,
    string? HisKey,
    decimal UnitPrice,
    decimal ContractQuantity,
    decimal ContractRemaining,
    decimal OpeningStock,
    decimal Received,
    decimal Issued,
    decimal CurrentStock);

public sealed record ProposalCandidatesDto(IReadOnlyList<ProposalCandidateDto> Items, bool HisAvailable, string HisName);

public sealed record ProposalLineRequest(Guid ContractItemId, decimal ProposedQuantity, string? Note);

public sealed record SaveProposalRequest(
    Guid DepartmentId,
    Guid WarehouseId,
    ProcurementDomain Domain,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string? Note,
    IReadOnlyList<ProposalLineRequest> Lines);

internal sealed class SaveProposalRequestValidator : AbstractValidator<SaveProposalRequest>
{
    public SaveProposalRequestValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty().WithName("Khoa/phòng");
        RuleFor(x => x.WarehouseId).NotEmpty().WithName("Kho");
        RuleFor(x => x.Domain).IsInEnum().WithName("Mảng");
        RuleFor(x => x.PeriodTo).GreaterThanOrEqualTo(x => x.PeriodFrom).WithName("Đến ngày");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Phiếu đề xuất phải có ít nhất một mặt hàng.");
        RuleForEach(x => x.Lines).ChildRules(l => l.RuleFor(x => x.ProposedQuantity).GreaterThan(0).WithName("SL đề xuất"));
    }
}

/// <summary>Phiếu đề xuất (spec §6.2 B1): khoa chọn mảng + hợp đồng, xem X–N–T từ HIS, nhập SL ≤ còn lại HĐ, ký số, gửi.</summary>
public sealed class ProposalService(
    IAppDbContext db,
    ProposalScope scope,
    DocumentNumbers numbers,
    ContractTracking tracking,
    ProposalDocumentData documentData,
    TemplateService templates,
    DocumentService documents,
    WorkflowService workflows,
    IHisClient his,
    ICurrentUser current)
{
    public const string OwnerType = "Proposal";

    public async Task<PagedResult<ProposalListItemDto>> ListAsync(PageQuery q, ProposalFilter f, CancellationToken ct)
    {
        var query = await VisibleAsync(ct);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(x => EF.Functions.ILike(x.Number, p) || x.Lines.Any(l => EF.Functions.ILike(l.ItemName, p)));
        }

        if (f.Domain is not null)
        {
            query = query.Where(x => x.Domain == f.Domain);
        }

        if (f.Status is not null)
        {
            query = query.Where(x => x.Status == f.Status);
        }

        if (f.DepartmentId is not null)
        {
            query = query.Where(x => x.DepartmentId == f.DepartmentId);
        }

        if (f.ContractId is not null)
        {
            query = query.Where(x => x.Lines.Any(l => l.ContractId == f.ContractId));
        }

        return await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new ProposalListItemDto(
                x.Id, x.Number, db.Departments.Where(d => d.Id == x.DepartmentId).Select(d => d.Name).FirstOrDefault() ?? string.Empty,
                x.Domain, x.PeriodText, x.Status, x.Lines.Count, x.Lines.Sum(l => l.ProposedQuantity), x.CreatedByName, x.CreatedAt, x.SubmittedAt,
                db.ProposalSummaries.Where(s => s.Id == x.SummaryId).Select(s => s.Number).FirstOrDefault()))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<ProposalDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await (await VisibleAsync(ct)).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null)
        {
            return Error.NotFound();
        }

        var department = await db.Departments.AsNoTracking().Where(d => d.Id == p.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(ct);
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == p.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct);
        var mine = (await scope.DepartmentsAsync(ct)).Contains(p.DepartmentId) && current.HasPermission(Permissions.ProposalCreate);
        return new ProposalDto(p.Id, p.Number, p.DepartmentId, department ?? string.Empty, p.WarehouseId, warehouse ?? string.Empty, p.Domain,
            p.PeriodFrom, p.PeriodTo, p.PeriodText, p.Note, p.Status, p.CreatedByName, p.DocumentId, p.SummaryId,
            [.. p.Lines.OrderBy(l => l.ContractNumber).ThenBy(l => l.ItemName).Select(l => new ProposalLineDto(
                l.Id, l.ContractId, l.ContractNumber, l.ContractItemId, l.ItemCode, l.ItemName, l.Unit, l.HisKey, l.OpeningStock, l.Received, l.Issued,
                l.CurrentStock, l.ContractRemaining, l.ProposedQuantity, l.Note))],
            CanEdit: mine && p.IsEditable,
            CanSubmit: mine && p.IsEditable && p.DocumentId is not null,
            CanCancel: mine && p.Status is ProposalStatus.Draft or ProposalStatus.Returned or ProposalStatus.Submitted);
    }

    /// <summary>Contract items of the domain with remaining quantity and X–N–T of the warehouse for the period (HIS).</summary>
    public async Task<Result<ProposalCandidatesDto>> CandidatesAsync(ProcurementDomain domain, Guid warehouseId, DateOnly from, DateOnly to, Guid? departmentId, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId, ct);
        if (warehouse is null)
        {
            return Error.Validation("WAREHOUSE_NOT_FOUND");
        }

        Guid? facilityId = departmentId is { } dep ? await db.Departments.Where(d => d.Id == dep).Select(d => (Guid?)d.FacilityId).FirstOrDefaultAsync(ct) : null;
        var rows = await scope.CallableContracts(domain).AsNoTracking()
            .Where(c => c.FacilityIds.Count == 0 || facilityId == null || c.FacilityIds.Contains(facilityId.Value))
            .SelectMany(c => c.Items.Select(i => new { Contract = c, Item = i }))
            .Where(x => x.Item.Quantity - x.Item.ReceivedQuantity > 0)
            .Select(x => new
            {
                x.Contract.Id,
                x.Contract.Number,
                SupplierName = db.Suppliers.Where(s => s.Id == x.Contract.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                x.Item,
                HisKey = db.Items.Where(i => i.Id == x.Item.ItemId).Select(i => i.HisKey).FirstOrDefault(),
            })
            .OrderBy(x => x.Number).ThenBy(x => x.Item.ItemName)
            .ToListAsync(ct);

        var keys = rows.Where(r => r.HisKey is not null).Select(r => r.HisKey!).Distinct().ToList();
        Dictionary<string, HisStockMovement> stock = [];
        var hisAvailable = true;
        if (keys.Count > 0 && warehouse.HisWarehouseCode is { } code)
        {
            try
            {
                stock = (await his.GetStockMovementAsync(code, keys, from, to, ct)).ToDictionary(s => s.HisKey);
            }
            catch (HisUnavailableException)
            {
                hisAvailable = false;
            }
        }

        var items = rows.Select(r =>
        {
            var s = r.HisKey is not null ? stock.GetValueOrDefault(r.HisKey) : null;
            return new ProposalCandidateDto(r.Id, r.Number, r.SupplierName, r.Item.Id, r.Item.ItemId, r.Item.ItemCode, r.Item.ItemName, r.Item.Unit, r.HisKey,
                r.Item.UnitPrice, r.Item.Quantity, r.Item.RemainingQuantity, s?.Opening ?? 0, s?.Received ?? 0, s?.Issued ?? 0, s?.Closing ?? 0);
        }).ToList();
        return new ProposalCandidatesDto(items, hisAvailable, his.DisplayName);
    }

    public async Task<Result<Guid>> CreateAsync(SaveProposalRequest r, CancellationToken ct)
    {
        if (!(await scope.DepartmentsAsync(ct)).Contains(r.DepartmentId))
        {
            return Error.Forbidden("PROPOSAL_DEPARTMENT_NOT_ALLOWED");
        }

        var facilityId = await db.Departments.Where(d => d.Id == r.DepartmentId).Select(d => (Guid?)d.FacilityId).FirstOrDefaultAsync(ct);
        var proposal = new Proposal(await numbers.NextAsync(DocumentNumbers.Proposal, ct), r.DepartmentId, facilityId, r.WarehouseId, r.Domain,
            r.PeriodFrom, r.PeriodTo, current.FullName);
        var applied = await ApplyAsync(proposal, r, ct);
        if (!applied.IsSuccess)
        {
            return applied.Error!;
        }

        db.Proposals.Add(proposal);
        var generated = await GenerateAsync(proposal, "Lập phiếu đề xuất", ct);
        if (!generated.IsSuccess)
        {
            return generated.Error!;
        }

        await db.SaveChangesAsync(ct);
        return proposal.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, SaveProposalRequest r, CancellationToken ct)
    {
        var proposal = await db.Proposals.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proposal is null || !(await scope.DepartmentsAsync(ct)).Contains(proposal.DepartmentId))
        {
            return Result.Failure(Error.NotFound());
        }

        if (r.DepartmentId != proposal.DepartmentId || r.Domain != proposal.Domain || r.WarehouseId != proposal.WarehouseId)
        {
            return Result.Failure(Error.Validation("PROPOSAL_HEADER_LOCKED"));
        }

        var applied = await ApplyAsync(proposal, r, ct);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        var generated = await GenerateAsync(proposal, "Cập nhật phiếu đề xuất", ct);
        if (!generated.IsSuccess)
        {
            return generated;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>"Ký và gửi": starts the proposal workflow (người lập ký số); resubmits after a return.</summary>
    public async Task<Result> SubmitAsync(Guid id, CancellationToken ct)
    {
        var proposal = await db.Proposals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proposal?.DocumentId is not { } docId || !(await scope.DepartmentsAsync(ct)).Contains(proposal.DepartmentId))
        {
            return Result.Failure(Error.NotFound());
        }

        if (!proposal.IsEditable)
        {
            return Result.Failure(Error.Conflict("PROPOSAL_LOCKED"));
        }

        var returned = await db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(w => w.DocumentId == docId && w.Status == WorkflowStatus.Returned, ct);
        if (returned is not null)
        {
            var resubmitted = await workflows.ResubmitAsync(returned.Id, ct);
            return resubmitted.IsSuccess ? Result.Success() : Result.Failure(resubmitted.Error!);
        }

        var started = await workflows.StartAsync(new StartWorkflowRequest(docId, null, null, proposal.FacilityId, proposal.Domain.ToString()), ct);
        return started.IsSuccess ? Result.Success() : Result.Failure(started.Error!);
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var proposal = await db.Proposals.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proposal is null || !(await scope.DepartmentsAsync(ct)).Contains(proposal.DepartmentId))
        {
            return Result.Failure(Error.NotFound());
        }

        proposal.Cancel();
        if (proposal.DocumentId is { } docId)
        {
            (await db.Documents.FirstAsync(d => d.Id == docId, ct)).Cancel();
        }

        await db.SaveChangesAsync(ct);
        await tracking.RecalculateAsync([.. proposal.Lines.Select(l => l.ContractItemId)], ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Rebuilds lines from server-side data (remaining quantity, HIS stock) — the client only sends quantities.</summary>
    private async Task<Result> ApplyAsync(Proposal proposal, SaveProposalRequest r, CancellationToken ct)
    {
        var candidates = await CandidatesAsync(r.Domain, r.WarehouseId, r.PeriodFrom, r.PeriodTo, r.DepartmentId, ct);
        if (!candidates.IsSuccess)
        {
            return Result.Failure(candidates.Error!);
        }

        var byItem = candidates.Value.Items.ToDictionary(c => c.ContractItemId);
        var drafts = new List<ProposalLineDraft>();
        foreach (var line in r.Lines)
        {
            if (!byItem.TryGetValue(line.ContractItemId, out var c))
            {
                return Result.Failure(Error.Validation("PROPOSAL_ITEM_NOT_CALLABLE"));
            }

            drafts.Add(new ProposalLineDraft(c.ContractId, c.ContractNumber, c.ContractItemId, c.ItemId, c.ItemCode, c.ItemName, c.Unit, c.HisKey,
                c.OpeningStock, c.Received, c.Issued, c.CurrentStock, c.ContractRemaining, line.ProposedQuantity, line.Note));
        }

        proposal.SetNote(r.Note);
        proposal.SetLines(drafts);
        return Result.Success();
    }

    private async Task<Result> GenerateAsync(Proposal proposal, string reason, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().Where(t => t.Kind == TemplateKind.Proposal && t.IsActive).OrderBy(t => t.Code).FirstOrDefaultAsync(ct);
        if (template is null)
        {
            return Result.Failure(Error.Validation("PROPOSAL_TEMPLATE_MISSING"));
        }

        var rendered = await templates.RenderAsync(template.Id, await documentData.ProposalAsync(proposal, ct), ct);
        if (!rendered.IsSuccess)
        {
            return Result.Failure(rendered.Error!);
        }

        if (proposal.DocumentId is { } docId)
        {
            var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
            await documents.AddDocxVersionAsync(document, rendered.Value, reason, current.UserId, current.FullName, ct);
        }
        else
        {
            var document = await documents.CreateFromDocxAsync(DocumentType.Proposal, $"Phiếu đề xuất {proposal.Number}", OwnerType, proposal.Id, null, null,
                template.Id, rendered.Value, reason, ct);
            proposal.AttachDocument(document.Id);
        }

        return Result.Success();
    }

    /// <summary>Department users see their departments' proposals; aggregators, domain heads and buyers see their domains.</summary>
    private async Task<IQueryable<Proposal>> VisibleAsync(CancellationToken ct)
    {
        var query = db.Proposals.AsNoTracking();
        if (scope.CanSeeAllDepartments)
        {
            var domains = scope.Domains();
            return domains.Count == 0 ? query : query.Where(p => domains.Contains(p.Domain));
        }

        var departments = await scope.DepartmentsAsync(ct);
        return query.Where(p => departments.Contains(p.DepartmentId));
    }
}
