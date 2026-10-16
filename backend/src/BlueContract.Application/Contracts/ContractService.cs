using System.Linq.Expressions;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Bids;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Organizations;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Contracts;

/// <summary>
/// Hợp đồng (spec §6.1 B2–B3, §6.6): drafting from a bid decision or outside bids, DOCX generation from the chosen template,
/// the signing workflow with the supplier review ↔ accountant loop, cancellation and appendices.
/// </summary>
public sealed class ContractService(
    IAppDbContext db,
    TemplateService templates,
    DocumentService documents,
    WorkflowService workflows,
    WorkflowAccess access,
    SupplierService suppliers,
    ContractDocumentData documentData,
    IDocxRevisions revisions,
    IPdfConverter pdf,
    IAuditLogger audit,
    ICurrentUser current,
    IClock clock,
    DataScope dataScope)
{
    public const string OwnerType = "Contract";
    public const string AppendixOwnerType = "ContractAppendix";
    private const int ExpiringSoonDays = 30;

    private static readonly Dictionary<string, Expression<Func<Contract, object?>>> Sorts = new()
    {
        ["number"] = x => x.Number,
        ["name"] = x => x.Name,
        ["totalAmount"] = x => x.TotalAmount,
        ["expiryDate"] = x => x.ExpiryDate,
        ["createdAt"] = x => x.CreatedAt,
    };

    // ---------------- Queries ----------------

    public async Task<PagedResult<ContractListItemDto>> ListAsync(PageQuery q, ContractFilter f, CancellationToken ct)
    {
        var query = await VisibleAsync(db.Contracts.AsNoTracking(), ct);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(c => EF.Functions.ILike(c.Number, p) || EF.Functions.ILike(c.Name, p)
                || db.Suppliers.Any(s => s.Id == c.SupplierId && (EF.Functions.ILike(s.Name, p) || EF.Functions.ILike(s.TaxCode, p))));
        }

        if (f.SupplierId is not null)
        {
            query = query.Where(c => c.SupplierId == f.SupplierId);
        }

        if (f.CategoryId is not null)
        {
            query = query.Where(c => c.CategoryId == f.CategoryId);
        }

        if (f.Status is not null)
        {
            query = query.Where(c => c.Status == f.Status);
        }

        if (f.ProcurementType is not null)
        {
            query = query.Where(c => c.ProcurementType == f.ProcurementType);
        }

        if (f.BidDecisionId is not null)
        {
            query = query.Where(c => c.BidDecisionId == f.BidDecisionId);
        }

        var today = Today();
        if (f.EffectiveOnly == true)
        {
            query = query.Where(c => c.Status == ContractStatus.Effective && (c.ExpiryDate == null || c.ExpiryDate >= today));
        }

        if (f.ExpiringSoon == true)
        {
            var limit = today.AddDays(ExpiringSoonDays);
            query = query.Where(c => c.Status == ContractStatus.Effective && c.ExpiryDate != null && c.ExpiryDate >= today && c.ExpiryDate <= limit);
        }

        return await query.ApplySort(q, Sorts, "createdAt", defaultDesc: true)
            .Select(c => new ContractListItemDto(
                c.Id, c.Number, c.Name, c.ProcurementType, c.Kind, c.SupplierId,
                db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                c.CategoryId,
                db.ItemCategories.Where(x => x.Id == c.CategoryId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                db.BidDecisions.Where(b => b.Id == c.BidDecisionId).Select(b => b.Number).FirstOrDefault(),
                c.Status, c.TotalAmount, c.EffectiveDate, c.ExpiryDate,
                c.Items.Sum(i => i.Quantity * i.UnitPrice) == 0 ? 0
                    : Math.Round(c.Items.Sum(i => i.ReceivedQuantity * i.UnitPrice) * 100 / c.Items.Sum(i => i.Quantity * i.UnitPrice), 1),
                c.RequiresWarehouseReceipt,
                c.CreatedAt))
            .ToPagedAsync(q, ct);
    }

    /// <summary>Reads, downloads and archive views are left out: the tab shows what happened to the contract.</summary>
    private static readonly string[] HistoryNoise = ["View", "Download", "OpenEditor", "ArchiveView", "ArchiveDownload", "ArchiveFileDownload"];

    /// <summary>Audit trail of the contract, its documents and appendices, newest first (spec §9.1-5 tab "Lịch sử").</summary>
    public async Task<Result<List<ContractHistoryEntryDto>>> HistoryAsync(Guid id, CancellationToken ct)
    {
        var contract = await (await VisibleAsync(db.Contracts.AsNoTracking(), ct)).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (contract is null)
        {
            return Error.NotFound();
        }

        var appendices = await db.ContractAppendices.AsNoTracking().Where(a => a.ContractId == id)
            .Select(a => new { a.Id, a.DocumentId }).ToListAsync(ct);
        var contractKey = id.ToString();
        var appendixKeys = appendices.Select(a => a.Id.ToString()).ToList();
        var documentKeys = appendices.Where(a => a.DocumentId != null).Select(a => a.DocumentId!.Value.ToString())
            .Concat(contract.DocumentId is { } doc ? [doc.ToString()] : Array.Empty<string>())
            .ToList();

        return await db.AuditLogs.AsNoTracking()
            .Where(a => !HistoryNoise.Contains(a.Action) &&
                ((a.EntityType == nameof(Contract) && a.EntityId == contractKey) ||
                 (a.EntityType == nameof(ContractAppendix) && appendixKeys.Contains(a.EntityId!)) ||
                 (a.EntityType == nameof(Document) && documentKeys.Contains(a.EntityId!))))
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(500)
            .Select(a => new ContractHistoryEntryDto(a.OccurredAt, a.UserName, a.Action, a.EntityType, a.Note, a.NewValues))
            .ToListAsync(ct);
    }

    public async Task<Result<ContractDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await (await VisibleAsync(db.Contracts.AsNoTracking(), ct)).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return Error.NotFound();
        }

        var supplier = await db.Suppliers.AsNoTracking().IgnoreQueryFilters().FirstAsync(s => s.Id == c.SupplierId, ct);
        var category = await db.ItemCategories.AsNoTracking().Where(x => x.Id == c.CategoryId).Select(x => x.Name).FirstAsync(ct);
        var facilityIds = c.FacilityIds;
        var facilityNames = await db.Facilities.AsNoTracking().Where(x => facilityIds.Contains(x.Id)).OrderBy(x => x.Code).Select(x => x.Name).ToListAsync(ct);
        var bidNumber = c.BidDecisionId is { } bid ? await db.BidDecisions.AsNoTracking().Where(b => b.Id == bid).Select(b => b.Number).FirstOrDefaultAsync(ct) : null;
        var templateName = c.TemplateId is { } tid ? await db.DocumentTemplates.AsNoTracking().Where(t => t.Id == tid).Select(t => t.Name).FirstOrDefaultAsync(ct) : null;
        var itemIds = c.Items.Select(i => i.ItemId).ToList();
        var itemInfo = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => new { i.HisKey, i.Specification }, ct);
        var appendices = await db.ContractAppendices.AsNoTracking().Include(a => a.Lines).Where(a => a.ContractId == id)
            .OrderBy(a => a.CreatedAt).ToListAsync(ct);

        var items = c.Items.OrderBy(i => i.ItemCode).Select(i => new ContractItemDto(
            i.Id, i.ItemId, i.ItemCode, i.ItemName, i.Unit, itemInfo.GetValueOrDefault(i.ItemId)?.HisKey, itemInfo.GetValueOrDefault(i.ItemId)?.Specification,
            i.Quantity, i.UnitPrice, i.VatRate, i.Amount, i.VatAmount, i.MaxUnitPrice, i.ProposedQuantity, i.ApprovedQuantity, i.CalledQuantity,
            i.ReceivedQuantity, i.AcceptedQuantity, i.PaidQuantity, i.RemainingQuantity)).ToList();
        var appendixDtos = appendices.Select(a => new ContractAppendixDto(a.Id, a.Number, a.Type, a.Reason, a.InfoChanges, a.OldExpiryDate, a.NewExpiryDate,
            a.ValueChange, a.Status, a.DocumentId, a.AppliedAt,
            [.. a.Lines.Select(l => new AppendixLineDto(l.ContractItemId, l.ItemName, l.OldQuantity, l.NewQuantity, l.OldUnitPrice, l.NewUnitPrice, l.ValueChange))],
            a.InfoFields))
            .ToList();

        return new ContractDto(c.Id, c.Number, c.Name, c.ProcurementType, c.Kind, c.RequiresWarehouseReceipt, c.BidDecisionId, bidNumber, c.SupplierId,
            supplier.Name, supplier.TaxCode, c.CategoryId, category, c.FacilityIds, facilityNames, c.SignedDate, c.EffectiveDate, c.ExpiryDate, c.Duration,
            c.AmountBeforeVat, c.VatAmount, c.TotalAmount, c.PaymentTerms, c.WarrantyTerms, c.DeliveryTerms, c.GuaranteeNumber, c.GuaranteeBank,
            c.GuaranteeAmount, c.GuaranteeExpiry, c.Note, c.TemplateId, templateName, c.DocumentId, c.Status, c.LastReturnReason, c.EffectiveAt,
            items, appendixDtos, await ActionsAsync(c, ct));
    }

    // ---------------- Drafting ----------------

    public async Task<Result<Guid>> CreateAsync(SaveContractRequest r, CancellationToken ct)
    {
        var number = r.Number.Trim();
        if (await db.Contracts.AnyAsync(c => c.Number == number, ct))
        {
            return Error.Conflict("CONTRACT_NUMBER_EXISTS");
        }

        var supplier = await ResolveSupplierAsync(r, ct);
        if (!supplier.IsSuccess)
        {
            return supplier.Error!;
        }

        var contract = new Contract(number, r.Name, r.ProcurementType, r.Kind, supplier.Value.Supplier.Id, r.CategoryId, r.BidDecisionId);
        var applied = await ApplyAsync(contract, r, ct);
        if (!applied.IsSuccess)
        {
            return applied.Error!;
        }

        db.Contracts.Add(contract);
        var generated = await GenerateDocumentAsync(contract, r.TemplateId, "Sinh hợp đồng từ mẫu biểu", ct);
        if (!generated.IsSuccess)
        {
            return generated.Error!;
        }

        audit.Log("ContractCreate", nameof(Contract), contract.Id.ToString(), new { contract.Number, contract.ProcurementType, contract.TotalAmount });
        await db.SaveChangesAsync(ct);

        if (supplier.Value.NewAccount is { } account)
        {
            await suppliers.SendCredentialsAsync(supplier.Value.Supplier, account.User, account.Password, ct);
        }

        return contract.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, SaveContractRequest r, CancellationToken ct)
    {
        var contract = await db.Contracts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!contract.IsEditable)
        {
            return Result.Failure(Error.Conflict("CONTRACT_LOCKED"));
        }

        var number = r.Number.Trim();
        if (await db.Contracts.AnyAsync(c => c.Id != id && c.Number == number, ct))
        {
            return Result.Failure(Error.Conflict("CONTRACT_NUMBER_EXISTS"));
        }

        if (r.SupplierId != contract.SupplierId || r.BidDecisionId != contract.BidDecisionId || r.ProcurementType != contract.ProcurementType)
        {
            return Result.Failure(Error.Validation("CONTRACT_PARTY_LOCKED"));
        }

        var applied = await ApplyAsync(contract, r, ct);
        if (!applied.IsSuccess)
        {
            return Result.Failure(applied.Error!);
        }

        if (r.RegenerateDocument || contract.DocumentId is null)
        {
            var generated = await GenerateDocumentAsync(contract, r.TemplateId, "Cập nhật nội dung hợp đồng (sinh lại từ mẫu)", ct);
            if (!generated.IsSuccess)
            {
                return Result.Failure(generated.Error!);
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Bước 4 của wizard: xem trước PDF với dữ liệu đang nhập (không lưu).</summary>
    public async Task<Result<byte[]>> PreviewAsync(SaveContractRequest r, CancellationToken ct)
    {
        Guid supplierId;
        if (r.SupplierId is { } sid)
        {
            supplierId = sid;
        }
        else
        {
            return Error.Validation("CONTRACT_PREVIEW_SUPPLIER_REQUIRED");
        }

        var contract = new Contract(string.IsNullOrWhiteSpace(r.Number) ? "…" : r.Number, string.IsNullOrWhiteSpace(r.Name) ? "…" : r.Name,
            r.ProcurementType, r.Kind, supplierId, r.CategoryId, r.BidDecisionId);
        var applied = await ApplyAsync(contract, r, ct, persistNewItems: false);
        if (!applied.IsSuccess)
        {
            return applied.Error!;
        }

        var rendered = await RenderAsync(contract, r.TemplateId, ct);
        return rendered.IsSuccess ? await pdf.ConvertToPdfAsync(rendered.Value, $"xem-truoc-{contract.Number}", ct) : rendered.Error!;
    }

    public async Task<Result> CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!contract.IsEditable)
        {
            return Result.Failure(Error.Conflict("CONTRACT_NOT_CANCELLABLE"));
        }

        if (contract.DocumentId is { } docId)
        {
            var wf = await db.WorkflowInstances.Include("_steps").Include("_events")
                .FirstOrDefaultAsync(w => w.DocumentId == docId && (w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.Returned), ct);
            wf?.Cancel(current.UserId!.Value, current.FullName ?? string.Empty, reason ?? "Hủy hợp đồng", clock.UtcNow);
            var document = await db.Documents.FirstAsync(d => d.Id == docId, ct);
            document.Cancel();
        }

        contract.Cancel(clock.UtcNow);
        audit.Log("ContractCancel", nameof(Contract), contract.Id.ToString(), null, reason);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------------- Signing workflow ----------------

    /// <summary>"Gửi rà soát": starts the contract workflow, or resubmits it after it was returned to the drafter.</summary>
    public async Task<Result> SubmitAsync(Guid id, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!contract.IsEditable || contract.DocumentId is null)
        {
            return Result.Failure(Error.Conflict("CONTRACT_NOT_SUBMITTABLE"));
        }

        var returned = await db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(w => w.DocumentId == contract.DocumentId && w.Status == WorkflowStatus.Returned, ct);
        if (returned is not null)
        {
            var resubmitted = await workflows.ResubmitAsync(returned.Id, ct);
            return resubmitted.IsSuccess ? Result.Success() : Result.Failure(resubmitted.Error!);
        }

        var domain = await db.ItemCategories.Where(c => c.Id == contract.CategoryId).Select(c => c.Domain).FirstAsync(ct);
        var started = await workflows.StartAsync(new StartWorkflowRequest(contract.DocumentId.Value, null, null, contract.SingleFacilityId, domain.ToString()), ct);
        return started.IsSuccess ? Result.Success() : Result.Failure(started.Error!);
    }

    /// <summary>
    /// NCC rà soát (menu Hợp đồng): "Đồng ý ký" when nothing was changed (skips the accountant), "Gửi lại" with tracked changes
    /// (or a comment) for the accountant to review.
    /// </summary>
    public async Task<Result> SupplierRespondAsync(Guid id, SupplierResponseRequest r, CancellationToken ct)
    {
        var loaded = await LoadReviewAsync(id, WorkflowActionType.SupplierReview, ct);
        if (!loaded.IsSuccess)
        {
            return Result.Failure(loaded.Error!);
        }

        var (_, document, _, step) = loaded.Value;
        var pending = await PendingRevisionsAsync(document, ct);
        if (r.Agree && pending > 0)
        {
            return Result.Failure(Error.Validation("CONTRACT_SUPPLIER_HAS_CHANGES"));
        }

        if (!r.Agree && pending == 0 && string.IsNullOrWhiteSpace(r.Comment))
        {
            return Result.Failure(Error.Validation("CONTRACT_SUPPLIER_NO_CHANGES"));
        }

        var comment = string.IsNullOrWhiteSpace(r.Comment)
            ? (r.Agree ? "Đồng ý nội dung hợp đồng, đề nghị ký" : $"Gửi lại {pending} đề xuất sửa đổi")
            : r.Comment.Trim();
        var approved = await workflows.ApproveAsync(step.Id, new ApproveStepRequest(comment, SkipNextOptional: r.Agree), ct);
        return approved.IsSuccess ? Result.Success() : Result.Failure(approved.Error!);
    }

    /// <summary>NCC tải lên bản DOCX đã sửa ngoài trình duyệt — bắt buộc bật Theo dõi thay đổi.</summary>
    public async Task<Result<Guid>> UploadSupplierRevisionAsync(Guid id, UploadedFile file, CancellationToken ct)
    {
        var loaded = await LoadReviewAsync(id, WorkflowActionType.SupplierReview, ct);
        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        if (FileInspector.Detect(file.Content) != FileKind.Docx)
        {
            return Error.Validation("TEMPLATE_NOT_DOCX");
        }

        IReadOnlyList<DocxRevision> found;
        try
        {
            found = revisions.List(file.Content);
        }
        catch (InvalidDataException)
        {
            return Error.Validation("TEMPLATE_NOT_DOCX");
        }

        if (found.Count == 0)
        {
            return Error.Validation("CONTRACT_REVISION_REQUIRED");
        }

        var document = loaded.Value.Document;
        var version = await documents.AddDocxVersionAsync(document, file.Content, $"NCC tải lên bản đề xuất sửa đổi ({found.Count} thay đổi)",
            current.UserId, current.FullName, ct);
        await db.SaveChangesAsync(ct);
        return version.Id;
    }

    /// <summary>Danh sách thay đổi (track changes) trong phiên bản hiện tại của hợp đồng.</summary>
    public async Task<Result<RevisionListDto>> RevisionsAsync(Guid id, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract?.DocumentId is not { } docId)
        {
            return Error.NotFound();
        }

        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
        var currentDocx = await documents.ReadCurrentDocxAsync(document, ct);
        if (currentDocx is not { } cd)
        {
            return Error.Validation("DOCUMENT_DOCX_REQUIRED");
        }

        return new RevisionListDto(document.Id, cd.Version.Id, cd.Version.VersionNumber, revisions.List(cd.Docx));
    }

    /// <summary>Kế toán chấp nhận / từ chối từng thay đổi của NCC (spec §6.1 B3-4) → phiên bản mới.</summary>
    public async Task<Result<Guid>> ResolveRevisionsAsync(Guid id, ResolveRevisionsRequest r, CancellationToken ct)
    {
        var loaded = await LoadReviewAsync(id, WorkflowActionType.Review, ct);
        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var document = loaded.Value.Document;
        var currentDocx = await documents.ReadCurrentDocxAsync(document, ct);
        if (currentDocx is not { } cd)
        {
            return Error.Validation("DOCUMENT_DOCX_REQUIRED");
        }

        if (cd.Version.Id != r.VersionId)
        {
            return Error.Conflict("DOCUMENT_CHANGED");
        }

        var decisions = r.Decisions.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.Last().Accept);
        if (decisions.Count == 0)
        {
            return Error.Validation("CONTRACT_REVISION_DECISION_REQUIRED");
        }

        var accepted = decisions.Count(d => d.Value);
        var resolved = revisions.Resolve(cd.Docx, decisions);
        var version = await documents.AddDocxVersionAsync(document, resolved,
            $"Kế toán xử lý thay đổi của NCC: chấp nhận {accepted}, từ chối {decisions.Count - accepted}", current.UserId, current.FullName, ct);
        audit.Log("ContractRevisions", nameof(Contract), id.ToString(), new { accepted, rejected = decisions.Count - accepted, version.VersionNumber });
        await db.SaveChangesAsync(ct);
        return version.Id;
    }

    // ---------------- Appendices ----------------

    /// <summary>One pending appendix per contract; its number is the next free one — both decided under a lock on the contract.</summary>
    public Task<Result<Guid>> CreateAppendixAsync(Guid contractId, SaveAppendixRequest r, CancellationToken ct) =>
        db.SerializedAsync([$"contract-appendix:{contractId}"], () => CreateAppendixLockedAsync(contractId, r, ct), ct);

    private async Task<Result<Guid>> CreateAppendixLockedAsync(Guid contractId, SaveAppendixRequest r, CancellationToken ct)
    {
        var contract = await db.Contracts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null)
        {
            return Error.NotFound();
        }

        if (await db.ContractAppendices.AnyAsync(a => a.ContractId == contractId && (a.Status == AppendixStatus.Draft || a.Status == AppendixStatus.InWorkflow), ct))
        {
            return Error.Conflict("APPENDIX_PENDING_EXISTS");
        }

        var count = await db.ContractAppendices.CountAsync(a => a.ContractId == contractId, ct);
        var number = string.IsNullOrWhiteSpace(r.Number) ? (count + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture) : r.Number.Trim();
        if (await AppendixBidCeilingAsync(contract, r.Type, [.. r.Lines.Select(l => (l.ContractItemId, l.NewQuantity))], ct) is { } overBid)
        {
            return overBid;
        }

        var appendix = new ContractAppendix(contract, number, r.Type, r.Reason);
        appendix.SetContent(contract, r.Reason, r.InfoChanges, r.NewExpiryDate, [.. r.Lines.Select(l => new AppendixLineDraft(l.ContractItemId, l.NewQuantity, l.NewUnitPrice))],
            r.InfoFields);
        db.ContractAppendices.Add(appendix);

        var generated = await GenerateAppendixDocumentAsync(contract, appendix, r.TemplateId, ct);
        if (!generated.IsSuccess)
        {
            return generated.Error!;
        }

        await db.SaveChangesAsync(ct);
        return appendix.Id;
    }

    public async Task<Result> UpdateAppendixAsync(Guid appendixId, SaveAppendixRequest r, CancellationToken ct)
    {
        var appendix = await db.ContractAppendices.Include(a => a.Lines).FirstOrDefaultAsync(a => a.Id == appendixId, ct);
        if (appendix is null)
        {
            return Result.Failure(Error.NotFound());
        }

        var contract = await db.Contracts.Include(c => c.Items).FirstAsync(c => c.Id == appendix.ContractId, ct);
        if (await AppendixBidCeilingAsync(contract, appendix.Type, [.. r.Lines.Select(l => (l.ContractItemId, l.NewQuantity))], ct) is { } overBid)
        {
            return Result.Failure(overBid);
        }

        appendix.SetContent(contract, r.Reason, r.InfoChanges, r.NewExpiryDate, [.. r.Lines.Select(l => new AppendixLineDraft(l.ContractItemId, l.NewQuantity, l.NewUnitPrice))],
            r.InfoFields);
        var generated = await GenerateAppendixDocumentAsync(contract, appendix, r.TemplateId, ct);
        if (!generated.IsSuccess)
        {
            return Result.Failure(generated.Error!);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SubmitAppendixAsync(Guid appendixId, CancellationToken ct)
    {
        var appendix = await db.ContractAppendices.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appendixId, ct);
        if (appendix?.DocumentId is not { } docId)
        {
            return Result.Failure(Error.NotFound());
        }

        if (appendix.Status != AppendixStatus.Draft)
        {
            return Result.Failure(Error.Conflict("APPENDIX_LOCKED"));
        }

        var returned = await db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(w => w.DocumentId == docId && w.Status == WorkflowStatus.Returned, ct);
        if (returned is not null)
        {
            var resubmitted = await workflows.ResubmitAsync(returned.Id, ct);
            return resubmitted.IsSuccess ? Result.Success() : Result.Failure(resubmitted.Error!);
        }

        var contract = await db.Contracts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == appendix.ContractId, ct);
        // Other contracts of the same bid may have grown since the draft: check the ceiling again before signing starts.
        var lines = await db.ContractAppendices.AsNoTracking().Where(a => a.Id == appendixId).SelectMany(a => a.Lines)
            .Select(l => new { l.ContractItemId, l.NewQuantity }).ToListAsync(ct);
        if (await AppendixBidCeilingAsync(contract, appendix.Type, [.. lines.Select(l => (l.ContractItemId, l.NewQuantity))], ct) is { } overBid)
        {
            return Result.Failure(overBid);
        }

        var started = await workflows.StartAsync(new StartWorkflowRequest(docId, null, null, contract.SingleFacilityId, null), ct);
        return started.IsSuccess ? Result.Success() : Result.Failure(started.Error!);
    }

    public async Task<Result> CancelAppendixAsync(Guid appendixId, CancellationToken ct)
    {
        var appendix = await db.ContractAppendices.FirstOrDefaultAsync(a => a.Id == appendixId, ct);
        if (appendix is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (appendix.Status == AppendixStatus.InWorkflow)
        {
            return Result.Failure(Error.Conflict("APPENDIX_IN_WORKFLOW"));
        }

        appendix.Cancel();
        if (appendix.DocumentId is { } docId)
        {
            (await db.Documents.FirstAsync(d => d.Id == docId, ct)).Cancel();
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------------- Helpers ----------------

    /// <summary>Suppliers see a contract only once the hospital sends it for their review (drafts stay internal).</summary>
    /// <summary>Suppliers see their contracts once sent to them; internal users see the contracts of their data scope.</summary>
    private async Task<IQueryable<Contract>> VisibleAsync(IQueryable<Contract> query, CancellationToken ct) =>
        current.IsSupplier
            ? query.Where(c => c.Status != ContractStatus.Draft && c.Status != ContractStatus.InReview && c.Status != ContractStatus.Returned)
            : await dataScope.ContractsAsync(query, ct);

    private async Task<Result<(Supplier Supplier, (User User, string Password)? NewAccount)>> ResolveSupplierAsync(SaveContractRequest r, CancellationToken ct)
    {
        if (r.SupplierId is { } id)
        {
            var existing = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
            return existing is null ? Error.Validation("SUPPLIER_NOT_FOUND") : (existing, null);
        }

        // Ngoài thầu: tạo NCC ngay trong form, kèm tài khoản quản trị NCC (spec §6.6).
        var n = r.NewSupplier!;
        var taxCode = Supplier.NormalizeTaxCode(n.TaxCode);
        if (await db.Suppliers.IgnoreQueryFilters().AnyAsync(s => s.TaxCode == taxCode, ct))
        {
            return Error.Conflict("SUPPLIER_TAX_CODE_EXISTS");
        }

        var supplier = new Supplier(taxCode, n.Name);
        supplier.Update(n.Name, n.ShortName, n.Address, n.RepresentativeName, n.RepresentativeTitle, n.AuthorizationDocument, n.BankAccountNumber,
            n.BankName, n.NotificationEmail, n.Phone, true);
        db.Suppliers.Add(supplier);
        (User, string)? account = null;
        if (n.AdminAccount is not null)
        {
            var created = await suppliers.CreateAccountAsync(supplier, n.AdminAccount, RoleCatalog.SupplierAdmin, ct);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            account = created.Value;
        }

        return (supplier, account);
    }

    /// <summary>Copies header/terms and rebuilds the goods list, enforcing bid quantities and prices.</summary>
    private async Task<Result> ApplyAsync(Contract contract, SaveContractRequest r, CancellationToken ct, bool persistNewItems = true)
    {
        if (!await db.ItemCategories.AnyAsync(c => c.Id == r.CategoryId, ct))
        {
            return Result.Failure(Error.Validation("ITEM_CATEGORY_NOT_FOUND"));
        }

        var facilityIds = (r.FacilityIds ?? []).Distinct().ToList();
        if (await db.Facilities.CountAsync(f => facilityIds.Contains(f.Id), ct) != facilityIds.Count)
        {
            return Result.Failure(Error.Validation("FACILITY_NOT_FOUND"));
        }

        contract.UpdateHeader(r.Number, r.Name, r.Kind, r.RequiresWarehouseReceipt, r.CategoryId, facilityIds, r.Note);
        contract.UpdateTerms(r.SignedDate, r.EffectiveDate, r.ExpiryDate, r.Duration, r.PaymentTerms, r.WarrantyTerms, r.DeliveryTerms,
            r.GuaranteeNumber, r.GuaranteeBank, r.GuaranteeAmount, r.GuaranteeExpiry);

        var lines = contract.ProcurementType == ProcurementType.Bidding
            ? await BidLinesAsync(contract, r, ct)
            : await NonBidLinesAsync(contract, r, persistNewItems, ct);
        if (!lines.IsSuccess)
        {
            return Result.Failure(lines.Error!);
        }

        contract.SetItems(lines.Value);
        return Result.Success();
    }

    private async Task<Result<List<ContractItemDraft>>> BidLinesAsync(Contract contract, SaveContractRequest r, CancellationToken ct)
    {
        var bid = await db.BidDecisions.AsNoTracking().Include(b => b.Suppliers).Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == contract.BidDecisionId, ct);
        if (bid is null)
        {
            return Error.Validation("BID_NOT_FOUND");
        }

        if (bid.Status != BidDecisionStatus.Active)
        {
            return Error.Validation("BID_NOT_ACTIVE");
        }

        if (bid.Suppliers.All(s => s.SupplierId != contract.SupplierId))
        {
            return Error.Validation("BID_SUPPLIER_NOT_WINNER");
        }

        var itemIds = r.Items.Where(l => l.ItemId is not null).Select(l => l.ItemId!.Value).ToList();
        if (itemIds.Count != r.Items.Count)
        {
            return Error.Validation("CONTRACT_BID_ITEM_REQUIRED");
        }

        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var contracted = await db.Contracts.AsNoTracking()
            .Where(c => c.BidDecisionId == bid.Id && c.Id != contract.Id && c.Status != ContractStatus.Cancelled)
            .SelectMany(c => c.Items).Where(i => itemIds.Contains(i.ItemId))
            .GroupBy(i => i.ItemId).Select(g => new { g.Key, Qty = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var drafts = new List<ContractItemDraft>();
        foreach (var line in r.Items)
        {
            var bidItem = bid.Items.FirstOrDefault(i => i.ItemId == line.ItemId && i.SupplierId == contract.SupplierId);
            if (bidItem is null || !items.TryGetValue(bidItem.ItemId, out var item))
            {
                return Error.Validation("CONTRACT_ITEM_NOT_IN_BID");
            }

            var remaining = bidItem.Quantity - contracted.GetValueOrDefault(bidItem.ItemId);
            if (line.Quantity > remaining)
            {
                return Error.Validation("CONTRACT_QTY_EXCEEDS_BID");
            }

            drafts.Add(new ContractItemDraft(item.Id, item.Code, item.Name, item.Unit, line.Quantity, line.UnitPrice, line.VatRate, bidItem.UnitPrice));
        }

        return drafts;
    }

    /// <summary>
    /// Phụ lục bổ sung số lượng của HĐ trong thầu: SL mới của mỗi mặt hàng + SL của các HĐ khác cùng quyết định thầu
    /// không vượt SL trúng thầu của NCC (same ceiling as when the contract was created).
    /// </summary>
    private async Task<Error?> AppendixBidCeilingAsync(Contract contract, AppendixType type, IReadOnlyList<(Guid ContractItemId, decimal? NewQuantity)> lines, CancellationToken ct)
    {
        if (type != AppendixType.AddQuantity || contract.BidDecisionId is not { } bidId)
        {
            return null;
        }

        var bidItems = await db.BidDecisions.AsNoTracking().Where(b => b.Id == bidId).SelectMany(b => b.Items)
            .Where(i => i.SupplierId == contract.SupplierId).ToListAsync(ct);
        var itemIds = contract.Items.Select(i => i.ItemId).ToList();
        var others = await db.Contracts.AsNoTracking()
            .Where(c => c.BidDecisionId == bidId && c.Id != contract.Id && c.Status != ContractStatus.Cancelled)
            .SelectMany(c => c.Items).Where(i => itemIds.Contains(i.ItemId))
            .GroupBy(i => i.ItemId).Select(g => new { g.Key, Qty = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        foreach (var (contractItemId, newQuantity) in lines)
        {
            var item = contract.Items.FirstOrDefault(i => i.Id == contractItemId);
            if (item is null || newQuantity is not { } wanted)
            {
                continue; // reported by the appendix itself
            }

            var bidItem = bidItems.FirstOrDefault(b => b.ItemId == item.ItemId);
            if (bidItem is null || wanted > bidItem.Quantity - others.GetValueOrDefault(item.ItemId))
            {
                return Error.Validation("APPENDIX_QTY_EXCEEDS_BID");
            }
        }

        return null;
    }

    private async Task<Result<List<ContractItemDraft>>> NonBidLinesAsync(Contract contract, SaveContractRequest r, bool persistNewItems, CancellationToken ct)
    {
        var itemIds = r.Items.Where(l => l.ItemId is not null).Select(l => l.ItemId!.Value).ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var drafts = new List<ContractItemDraft>();
        foreach (var line in r.Items)
        {
            Item item;
            if (line.ItemId is { } itemId)
            {
                if (!items.TryGetValue(itemId, out var found) || (found.SupplierId is { } owner && owner != contract.SupplierId && found.BidDecisionId is not null))
                {
                    return Error.Validation("CONTRACT_ITEM_NOT_FOUND");
                }

                item = found;
            }
            else
            {
                var n = line.NewItem!;
                item = new Item(n.Code, n.Name, n.Unit, contract.CategoryId, ItemSource.Manual);
                item.UpdateDetails(n.Name, n.Unit, n.ActiveIngredient, null, n.Specification, n.Manufacturer, n.Country, null, null, true);
                item.SetHisKey(n.HisKey);
                item.AssignToSupplier(contract.SupplierId);
                item.SetFacility(contract.SingleFacilityId);
                if (persistNewItems)
                {
                    db.Items.Add(item);
                }
            }

            drafts.Add(new ContractItemDraft(item.Id, item.Code, item.Name, item.Unit, line.Quantity, line.UnitPrice, line.VatRate, null));
        }

        return drafts;
    }

    private async Task<Result<byte[]>> RenderAsync(Contract contract, Guid templateId, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null || template.Kind != TemplateKind.Contract || !template.IsActive)
        {
            return Error.Validation("CONTRACT_TEMPLATE_INVALID");
        }

        return await templates.RenderAsync(templateId, await documentData.BuildAsync(contract, ct), ct);
    }

    private async Task<Result> GenerateDocumentAsync(Contract contract, Guid templateId, string reason, CancellationToken ct)
    {
        var rendered = await RenderAsync(contract, templateId, ct);
        if (!rendered.IsSuccess)
        {
            return Result.Failure(rendered.Error!);
        }

        var title = $"Hợp đồng số {contract.Number}";
        if (contract.DocumentId is { } docId)
        {
            var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
            document.Rename(title);
            await documents.AddDocxVersionAsync(document, rendered.Value, reason, current.UserId, current.FullName, ct);
            contract.AttachDocument(document.Id, templateId);
        }
        else
        {
            var document = await documents.CreateFromDocxAsync(DocumentType.Contract, title, OwnerType, contract.Id, contract.SupplierId, contract.Id,
                templateId, rendered.Value, reason, ct);
            document.LinkBidDecision(contract.BidDecisionId);
            contract.AttachDocument(document.Id, templateId);
        }

        return Result.Success();
    }

    private async Task<Result> GenerateAppendixDocumentAsync(Contract contract, ContractAppendix appendix, Guid? templateId, CancellationToken ct)
    {
        var template = templateId is { } tid
            ? await db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid && t.Kind == TemplateKind.Appendix && t.IsActive, ct)
            : await db.DocumentTemplates.AsNoTracking().Where(t => t.Kind == TemplateKind.Appendix && t.IsActive).OrderBy(t => t.Code).FirstOrDefaultAsync(ct);
        if (template is null)
        {
            return Result.Failure(Error.Validation("APPENDIX_TEMPLATE_INVALID"));
        }

        var rendered = await templates.RenderAsync(template.Id, await documentData.BuildAppendixAsync(contract, appendix, ct), ct);
        if (!rendered.IsSuccess)
        {
            return Result.Failure(rendered.Error!);
        }

        var title = $"Phụ lục số {appendix.Number} — Hợp đồng {contract.Number}";
        if (appendix.DocumentId is { } docId)
        {
            var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
            await documents.AddDocxVersionAsync(document, rendered.Value, "Cập nhật nội dung phụ lục", current.UserId, current.FullName, ct);
        }
        else
        {
            var document = await documents.CreateFromDocxAsync(DocumentType.Appendix, title, AppendixOwnerType, appendix.Id, contract.SupplierId, contract.Id,
                template.Id, rendered.Value, "Sinh phụ lục từ mẫu biểu", ct);
            document.LinkBidDecision(contract.BidDecisionId);
            appendix.AttachDocument(document.Id);
        }

        return Result.Success();
    }

    /// <summary>Loads the contract's running workflow whose active step is of the given kind and assigned to the caller.</summary>
    private async Task<Result<(Contract Contract, Document Document, WorkflowInstance Workflow, WorkflowStepInstance Step)>> LoadReviewAsync(
        Guid id, WorkflowActionType action, CancellationToken ct)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract?.DocumentId is not { } docId)
        {
            return Error.NotFound();
        }

        var wf = await db.WorkflowInstances.Include("_steps").Include("_events")
            .FirstOrDefaultAsync(w => w.DocumentId == docId && w.Status == WorkflowStatus.Running, ct);
        if (wf?.ActiveStep is not { } step || step.ActionType != action || (action == WorkflowActionType.Review && !step.AllowEdit))
        {
            return Error.Conflict("WORKFLOW_STEP_NOT_ACTIVE");
        }

        var standing = await access.StandingAsync(wf, step, ct);
        if (!standing.CanAct)
        {
            return Error.Forbidden(standing.DenyReason ?? "FORBIDDEN");
        }

        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
        return (contract, document, wf, step);
    }

    private async Task<int> PendingRevisionsAsync(Document document, CancellationToken ct)
    {
        var currentDocx = await documents.ReadCurrentDocxAsync(document, ct);
        return currentDocx is { } cd ? revisions.List(cd.Docx).Count : 0;
    }

    private async Task<ContractActionsDto> ActionsAsync(Contract c, CancellationToken ct)
    {
        var canCreate = current.HasPermission(Permissions.ContractCreate);
        Guid? supplierStep = null;
        Guid? accountantStep = null;
        int? supplierOrder = null;
        var pending = 0;
        if (c.DocumentId is { } docId)
        {
            var wf = await db.WorkflowInstances.AsNoTracking().Include("_steps")
                .FirstOrDefaultAsync(w => w.DocumentId == docId && w.Status == WorkflowStatus.Running, ct);
            if (wf?.ActiveStep is { } active)
            {
                supplierOrder = wf.Steps.FirstOrDefault(s => s.ActionType == WorkflowActionType.SupplierReview)?.Order;
                var reviewStep = active.ActionType == WorkflowActionType.SupplierReview
                    || (active.ActionType == WorkflowActionType.Review && active.AllowEdit && supplierOrder is not null && active.Order > supplierOrder);
                if (reviewStep && (await access.StandingAsync(wf, active, ct)).CanAct)
                {
                    if (active.ActionType == WorkflowActionType.SupplierReview)
                    {
                        supplierStep = active.Id;
                    }
                    else
                    {
                        accountantStep = active.Id;
                    }

                    var document = await db.Documents.AsNoTracking().Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
                    pending = await PendingRevisionsAsync(document, ct);
                }
            }
        }

        return new ContractActionsDto(
            CanEdit: c.IsEditable && canCreate,
            CanSubmit: c.IsEditable && canCreate && c.DocumentId is not null,
            CanCancel: c.IsEditable && canCreate,
            CanCreateAppendix: c.IsEffective && canCreate,
            supplierStep,
            accountantStep,
            supplierOrder,
            pending);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
}
