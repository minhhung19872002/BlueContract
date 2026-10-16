using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Integration;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Settings;
using BlueContract.Domain.Workflows;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Proposals;

public sealed record SummaryListItemDto(
    Guid Id,
    string Number,
    ProcurementDomain Domain,
    string PeriodText,
    ProposalSummaryStatus Status,
    string? AggregatorName,
    int ProposalCount,
    int LineCount,
    decimal TotalAmount,
    decimal RemainingToCallAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt);

public sealed record SummaryFilter(ProcurementDomain? Domain, ProposalSummaryStatus? Status, bool? Callable);

public sealed record SummaryLineDto(
    Guid Id,
    Guid ContractId,
    string ContractNumber,
    Guid ContractItemId,
    Guid SupplierId,
    string SupplierName,
    string ItemCode,
    string ItemName,
    string Unit,
    decimal UnitPrice,
    decimal ProposedQuantity,
    decimal OpeningStock,
    decimal Received,
    decimal Issued,
    decimal CurrentStock,
    decimal ContractRemaining,
    decimal ApprovedQuantity,
    decimal CalledQuantity,
    decimal RemainingToCall,
    decimal Amount);

public sealed record SummaryProposalDto(Guid Id, string Number, string DepartmentName, int LineCount);

public sealed record SummaryDto(
    Guid Id,
    string Number,
    ProcurementDomain Domain,
    Guid? FacilityId,
    string PeriodText,
    string? AggregatorName,
    string? Note,
    ProposalSummaryStatus Status,
    Guid? DocumentId,
    DateTimeOffset? ApprovedAt,
    IReadOnlyList<SummaryProposalDto> Proposals,
    IReadOnlyList<SummaryLineDto> Lines,
    bool CanEdit,
    bool CanSubmit,
    bool CanCancel,
    bool CanEditApproved,
    bool CanCall);

public sealed record SaveSummaryRequest(ProcurementDomain Domain, Guid? FacilityId, string PeriodText, string? Note, IReadOnlyList<Guid> ProposalIds);

public sealed record ApprovedQuantityRequest(Guid LineId, decimal ApprovedQuantity);

internal sealed class SaveSummaryRequestValidator : AbstractValidator<SaveSummaryRequest>
{
    public SaveSummaryRequestValidator()
    {
        RuleFor(x => x.Domain).IsInEnum().WithName("Mảng");
        RuleFor(x => x.PeriodText).NotEmpty().WithName("Kỳ tổng hợp").MaximumLength(100);
        RuleFor(x => x.ProposalIds).NotEmpty().WithMessage("Chọn ít nhất một phiếu đề xuất.");
    }
}

/// <summary>Tổng hợp đề xuất (spec §6.2 B2–B3): gộp theo mặt hàng, X–N–T từ HIS, tổ trưởng sửa SL duyệt, Giám đốc ký.</summary>
public sealed class ProposalSummaryService(
    IAppDbContext db,
    ProposalScope scope,
    DocumentNumbers numbers,
    ContractTracking tracking,
    ProposalDocumentData documentData,
    TemplateService templates,
    DocumentService documents,
    WorkflowService workflows,
    WorkflowAccess access,
    IHisClient his,
    ICurrentUser current,
    Admin.SettingService settings)
{
    public const string OwnerType = "ProposalSummary";

    public Task<PagedResult<SummaryListItemDto>> ListAsync(PageQuery q, SummaryFilter f, CancellationToken ct)
    {
        var query = Visible();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(s => EF.Functions.ILike(s.Number, p) || EF.Functions.ILike(s.PeriodText, p));
        }

        if (f.Domain is not null)
        {
            query = query.Where(s => s.Domain == f.Domain);
        }

        if (f.Status is not null)
        {
            query = query.Where(s => s.Status == f.Status);
        }

        if (f.Callable == true)
        {
            query = query.Where(s => s.Status == ProposalSummaryStatus.Approved && s.Lines.Any(l => l.ApprovedQuantity > l.CalledQuantity));
        }

        return query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(s => new SummaryListItemDto(
                s.Id, s.Number, s.Domain, s.PeriodText, s.Status, s.AggregatorName, s.Sources.Count, s.Lines.Count,
                s.Lines.Sum(l => l.ApprovedQuantity * l.UnitPrice),
                s.Lines.Sum(l => (l.ApprovedQuantity - l.CalledQuantity) * l.UnitPrice),
                s.CreatedAt, s.ApprovedAt))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<SummaryDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await Visible().Include(x => x.Lines).Include(x => x.Sources).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
        {
            return Error.NotFound();
        }

        var proposalIds = s.Sources.Select(x => x.ProposalId).ToList();
        var proposals = await db.Proposals.AsNoTracking().Where(p => proposalIds.Contains(p.Id))
            .Select(p => new SummaryProposalDto(p.Id, p.Number, db.Departments.Where(d => d.Id == p.DepartmentId).Select(d => d.Name).FirstOrDefault() ?? string.Empty, p.Lines.Count))
            .ToListAsync(ct);
        var canAggregate = current.HasPermission(Permissions.ProposalAggregate) && s.AggregatorId == current.UserId;
        return new SummaryDto(s.Id, s.Number, s.Domain, s.FacilityId, s.PeriodText, s.AggregatorName, s.Note, s.Status, s.DocumentId, s.ApprovedAt, proposals,
            [.. s.Lines.OrderBy(l => l.SupplierName).ThenBy(l => l.ItemName).Select(l => new SummaryLineDto(
                l.Id, l.ContractId, l.ContractNumber, l.ContractItemId, l.SupplierId, l.SupplierName, l.ItemCode, l.ItemName, l.Unit, l.UnitPrice,
                l.ProposedQuantity, l.OpeningStock, l.Received, l.Issued, l.CurrentStock, l.ContractRemaining, l.ApprovedQuantity, l.CalledQuantity,
                l.RemainingToCall, l.Amount))],
            CanEdit: canAggregate && s.IsEditable,
            CanSubmit: canAggregate && s.IsEditable && s.DocumentId is not null,
            CanCancel: canAggregate && (s.IsEditable || (s.Status == ProposalSummaryStatus.Approved && s.Lines.All(l => l.CalledQuantity == 0))),
            CanEditApproved: await DomainHeadStepAsync(s, ct) is not null,
            CanCall: s.Status == ProposalSummaryStatus.Approved && current.HasPermission(Permissions.CallOrderCreate) && s.Lines.Any(l => l.RemainingToCall > 0));
    }

    /// <summary>Phiếu đề xuất đã gửi, chưa tổng hợp, thuộc mảng được phân công.</summary>
    public async Task<List<ProposalListItemDto>> CandidatesAsync(ProcurementDomain domain, Guid? facilityId, CancellationToken ct)
    {
        var domains = scope.Domains();
        if (domains.Count > 0 && !domains.Contains(domain))
        {
            return [];
        }

        return await db.Proposals.AsNoTracking()
            .Where(p => p.Domain == domain && p.Status == ProposalStatus.Submitted && (facilityId == null || p.FacilityId == facilityId))
            .OrderBy(p => p.SubmittedAt)
            .Select(p => new ProposalListItemDto(
                p.Id, p.Number, db.Departments.Where(d => d.Id == p.DepartmentId).Select(d => d.Name).FirstOrDefault() ?? string.Empty,
                p.Domain, p.PeriodText, p.Status, p.Lines.Count, p.Lines.Sum(l => l.ProposedQuantity), p.CreatedByName, p.CreatedAt, p.SubmittedAt, null))
            .ToListAsync(ct);
    }

    public async Task<Result<Guid>> CreateAsync(SaveSummaryRequest r, CancellationToken ct)
    {
        var domains = scope.Domains();
        if (domains.Count > 0 && !domains.Contains(r.Domain))
        {
            return Error.Forbidden("WORKFLOW_OUT_OF_SCOPE");
        }

        var summary = new ProposalSummary(await numbers.NextAsync(DocumentNumbers.Summary, ct), r.Domain, r.FacilityId, r.PeriodText, current.UserId!.Value, current.FullName);
        var applied = await ApplyAsync(summary, r, ct);
        if (!applied.IsSuccess)
        {
            return applied.Error!;
        }

        db.ProposalSummaries.Add(summary);
        var generated = await GenerateAsync(summary, "Lập phiếu tổng hợp đề xuất", ct);
        if (!generated.IsSuccess)
        {
            return generated.Error!;
        }

        await db.SaveChangesAsync(ct);
        return summary.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, SaveSummaryRequest r, CancellationToken ct)
    {
        var summary = await db.ProposalSummaries.Include(s => s.Lines).Include(s => s.Sources).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (summary is null || summary.AggregatorId != current.UserId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (r.Domain != summary.Domain)
        {
            return Result.Failure(Error.Validation("SUMMARY_DOMAIN_LOCKED"));
        }

        // Proposals removed from the selection become available again.
        var removed = summary.Sources.Select(x => x.ProposalId).Except(r.ProposalIds).ToList();
        foreach (var p in await db.Proposals.Where(p => removed.Contains(p.Id)).ToListAsync(ct))
        {
            p.ReleaseFromSummary();
        }

        var applied = await ApplyAsync(summary, r, ct);
        if (!applied.IsSuccess)
        {
            return applied;
        }

        var generated = await GenerateAsync(summary, "Cập nhật phiếu tổng hợp", ct);
        if (!generated.IsSuccess)
        {
            return generated;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Gửi ký duyệt: người tổng hợp ký → tổ trưởng duyệt → Giám đốc ký.</summary>
    public async Task<Result> SubmitAsync(Guid id, CancellationToken ct)
    {
        var summary = await db.ProposalSummaries.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (summary?.DocumentId is not { } docId || summary.AggregatorId != current.UserId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!summary.IsEditable)
        {
            return Result.Failure(Error.Conflict("SUMMARY_LOCKED"));
        }

        var returned = await db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(w => w.DocumentId == docId && w.Status == WorkflowStatus.Returned, ct);
        if (returned is not null)
        {
            var resubmitted = await workflows.ResubmitAsync(returned.Id, ct);
            return resubmitted.IsSuccess ? Result.Success() : Result.Failure(resubmitted.Error!);
        }

        var started = await workflows.StartAsync(new StartWorkflowRequest(docId, null, null, summary.FacilityId, summary.Domain.ToString()), ct);
        return started.IsSuccess ? Result.Success() : Result.Failure(started.Error!);
    }

    /// <summary>
    /// Tổ trưởng sửa SL duyệt (spec §6.2 B3). The aggregator already signed the current version, so the new version restarts
    /// the workflow and the aggregator signs again (ADR-036); reordering the steps avoids the re-signing.
    /// </summary>
    public async Task<Result> SetApprovedAsync(Guid id, IReadOnlyList<ApprovedQuantityRequest> lines, CancellationToken ct)
    {
        var summary = await db.ProposalSummaries.Include(s => s.Lines).Include(s => s.Sources).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (summary is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (await DomainHeadStepAsync(summary, ct) is null)
        {
            return Result.Failure(Error.Forbidden("SUMMARY_NOT_IN_REVIEW"));
        }

        var changed = false;
        foreach (var line in lines)
        {
            var current0 = summary.Lines.FirstOrDefault(l => l.Id == line.LineId) ?? throw new Domain.Common.DomainException("SUMMARY_LINE_NOT_FOUND");
            if (current0.ApprovedQuantity != line.ApprovedQuantity)
            {
                summary.SetApprovedQuantity(line.LineId, line.ApprovedQuantity);
                changed = true;
            }
        }

        if (!changed)
        {
            return Result.Success();
        }

        // Spec §6.2 B3: configurable — back to the aggregator for a new signature, or on to the director.
        var resign = await settings.GetBoolAsync(SystemSettingKeys.SummaryResignAfterHeadEdit, true, ct);
        var generated = await GenerateAsync(summary, "Tổ trưởng điều chỉnh số lượng duyệt", ct, restartWorkflow: resign);
        if (!generated.IsSuccess)
        {
            return generated;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var summary = await db.ProposalSummaries.Include(s => s.Lines).Include(s => s.Sources).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (summary is null || summary.AggregatorId != current.UserId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (summary.Status is not (ProposalSummaryStatus.Draft or ProposalSummaryStatus.Returned or ProposalSummaryStatus.Approved))
        {
            return Result.Failure(Error.Conflict("SUMMARY_LOCKED"));
        }

        summary.Cancel();
        var ids = summary.Sources.Select(x => x.ProposalId).ToList();
        foreach (var p in await db.Proposals.Where(p => ids.Contains(p.Id)).ToListAsync(ct))
        {
            p.ReleaseFromSummary();
        }

        if (summary.DocumentId is { } docId)
        {
            (await db.Documents.FirstAsync(d => d.Id == docId, ct)).Cancel();
        }

        await db.SaveChangesAsync(ct);
        await tracking.RecalculateAsync([.. summary.Lines.Select(l => l.ContractItemId)], ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> ApplyAsync(ProposalSummary summary, SaveSummaryRequest r, CancellationToken ct)
    {
        var proposals = await db.Proposals.Include(p => p.Lines).Where(p => r.ProposalIds.Contains(p.Id)).ToListAsync(ct);
        if (proposals.Count != r.ProposalIds.Distinct().Count())
        {
            return Result.Failure(Error.Validation("SUMMARY_PROPOSAL_NOT_FOUND"));
        }

        if (proposals.Any(p => p.Domain != r.Domain || !(p.Status == ProposalStatus.Submitted || (p.Status == ProposalStatus.Aggregated && p.SummaryId == summary.Id))))
        {
            return Result.Failure(Error.Validation("SUMMARY_PROPOSAL_NOT_AVAILABLE"));
        }

        var lines = proposals.SelectMany(p => p.Lines.Select(l => new { Proposal = p, Line = l })).ToList();
        var itemIds = lines.Select(x => x.Line.ContractItemId).Distinct().ToList();
        var contractItems = await db.Contracts.AsNoTracking().SelectMany(c => c.Items.Select(i => new { Contract = c, Item = i }))
            .Where(x => itemIds.Contains(x.Item.Id))
            .Select(x => new
            {
                x.Item.Id,
                ContractId = x.Contract.Id,
                x.Contract.Number,
                x.Contract.SupplierId,
                SupplierName = db.Suppliers.Where(s => s.Id == x.Contract.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                x.Item.ItemCode,
                x.Item.ItemName,
                x.Item.Unit,
                x.Item.UnitPrice,
                Remaining = x.Item.Quantity - x.Item.ReceivedQuantity,
            })
            .ToDictionaryAsync(x => x.Id, ct);

        // X–N–T from the HIS for every warehouse of the merged proposals, summed per item (fallback: the proposals' snapshot).
        var stock = await StockAsync(proposals, ct);
        var drafts = lines.GroupBy(x => x.Line.ContractItemId).Select(g =>
        {
            var ci = contractItems[g.Key];
            var hisKey = g.First().Line.HisKey;
            var s = hisKey is not null && stock.TryGetValue(hisKey, out var v) ? v
                : (g.Sum(x => x.Line.OpeningStock), g.Sum(x => x.Line.Received), g.Sum(x => x.Line.Issued), g.Sum(x => x.Line.CurrentStock));
            return new SummaryLineDraft(ci.ContractId, ci.Number, ci.Id, ci.SupplierId, ci.SupplierName, ci.ItemCode, ci.ItemName, ci.Unit, ci.UnitPrice,
                g.Sum(x => x.Line.ProposedQuantity), s.Item1, s.Item2, s.Item3, s.Item4, ci.Remaining);
        }).ToList();

        summary.SetContent(r.ProposalIds, drafts, r.Note);
        foreach (var p in proposals.Where(p => p.Status == ProposalStatus.Submitted))
        {
            p.MarkAggregated(summary.Id);
        }

        return Result.Success();
    }

    private async Task<Dictionary<string, (decimal, decimal, decimal, decimal)>> StockAsync(List<Proposal> proposals, CancellationToken ct)
    {
        var result = new Dictionary<string, (decimal, decimal, decimal, decimal)>();
        var from = proposals.Min(p => p.PeriodFrom);
        var to = proposals.Max(p => p.PeriodTo);
        var keys = proposals.SelectMany(p => p.Lines).Where(l => l.HisKey is not null).Select(l => l.HisKey!).Distinct().ToList();
        var warehouseIds = proposals.Select(p => p.WarehouseId).Distinct().ToList();
        var codes = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id) && w.HisWarehouseCode != null)
            .Select(w => w.HisWarehouseCode!).ToListAsync(ct);
        try
        {
            foreach (var code in codes)
            {
                foreach (var s in await his.GetStockMovementAsync(code, keys, from, to, ct))
                {
                    var prev = result.GetValueOrDefault(s.HisKey);
                    result[s.HisKey] = (prev.Item1 + s.Opening, prev.Item2 + s.Received, prev.Item3 + s.Issued, prev.Item4 + s.Closing);
                }
            }
        }
        catch (HisUnavailableException)
        {
            return [];
        }

        return result;
    }

    private async Task<Result> GenerateAsync(ProposalSummary summary, string reason, CancellationToken ct, bool restartWorkflow = true)
    {
        var template = await db.DocumentTemplates.AsNoTracking().Where(t => t.Kind == TemplateKind.ProposalSummary && t.IsActive).OrderBy(t => t.Code).FirstOrDefaultAsync(ct);
        if (template is null)
        {
            return Result.Failure(Error.Validation("SUMMARY_TEMPLATE_MISSING"));
        }

        var rendered = await templates.RenderAsync(template.Id, await documentData.SummaryAsync(summary, ct), ct);
        if (!rendered.IsSuccess)
        {
            return Result.Failure(rendered.Error!);
        }

        if (summary.DocumentId is { } docId)
        {
            var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
            await documents.AddDocxVersionAsync(document, rendered.Value, reason, current.UserId, current.FullName, ct, restartWorkflow);
        }
        else
        {
            var document = await documents.CreateFromDocxAsync(DocumentType.ProposalSummary, $"Phiếu tổng hợp đề xuất {summary.Number}", OwnerType, summary.Id,
                null, null, template.Id, rendered.Value, reason, ct);
            summary.AttachDocument(document.Id);
        }

        return Result.Success();
    }

    /// <summary>The active "tổ trưởng" step when the caller may act on it.</summary>
    private async Task<WorkflowStepInstance?> DomainHeadStepAsync(ProposalSummary summary, CancellationToken ct)
    {
        if (summary.DocumentId is not { } docId || summary.Status != ProposalSummaryStatus.DomainHeadReview)
        {
            return null;
        }

        var wf = await db.WorkflowInstances.Include("_steps").FirstOrDefaultAsync(w => w.DocumentId == docId && w.Status == WorkflowStatus.Running, ct);
        return wf?.ActiveStep is { AllowEdit: true } step && (await access.StandingAsync(wf, step, ct)).CanAct ? step : null;
    }

    private IQueryable<ProposalSummary> Visible()
    {
        var domains = scope.Domains();
        var query = db.ProposalSummaries.AsNoTracking();
        return domains.Count == 0 ? query : query.Where(s => domains.Contains(s.Domain));
    }
}
