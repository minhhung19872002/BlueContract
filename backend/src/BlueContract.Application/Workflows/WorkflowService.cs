using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Notifications;
using BlueContract.Domain.Common;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Workflows;

/// <summary>
/// Workflow engine use cases (spec §5.7, §6.7): configure templates, start on a document, approve / return / comment,
/// inbox ("Trình ký"). Signing steps are completed by <see cref="Signing.SigningService"/>.
/// </summary>
public sealed class WorkflowService(
    IAppDbContext db,
    WorkflowAccess access,
    DocumentService documents,
    NotificationService notifications,
    IAuditLogger audit,
    ICurrentUser current,
    IClock clock,
    IEnumerable<IWorkflowObserver> observers)
{
    // ---------------- Templates (Cấu hình luồng ký) ----------------

    public async Task<List<WorkflowTemplateDto>> ListTemplatesAsync(DocumentType? type, CancellationToken ct)
    {
        var query = db.WorkflowTemplates.AsNoTracking().Include("_steps").AsQueryable();
        if (type is not null)
        {
            query = query.Where(t => t.DocumentType == type);
        }

        var templates = await query.OrderBy(t => t.DocumentType).ThenBy(t => t.Code).ToListAsync(ct);
        var running = await db.WorkflowInstances.IgnoreQueryFilters()
            .Where(w => w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.Returned)
            .GroupBy(w => w.TemplateId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var userIds = templates.SelectMany(t => t.Steps).Where(s => s.UserId != null).Select(s => s.UserId!.Value).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return templates.Select(t => ToDto(t, running.GetValueOrDefault(t.Id), names)).ToList();
    }

    public async Task<Result<Guid>> SaveTemplateAsync(Guid? id, SaveWorkflowTemplateRequest r, CancellationToken ct)
    {
        WorkflowTemplate template;
        if (id is null)
        {
            var code = r.Code.Trim().ToUpperInvariant();
            if (await db.WorkflowTemplates.IgnoreQueryFilters().AnyAsync(t => t.Code == code, ct))
            {
                return Error.Conflict("WORKFLOW_CODE_EXISTS");
            }

            template = new WorkflowTemplate(code, r.Name, r.DocumentType, r.Condition, r.IsDefault);
            db.WorkflowTemplates.Add(template);
        }
        else
        {
            template = await db.WorkflowTemplates.Include("_steps").FirstOrDefaultAsync(t => t.Id == id, ct)
                ?? throw new DomainException("NOT_FOUND");
            template.Update(r.Name, r.Condition, r.IsDefault, r.IsActive);
        }

        var userIds = r.Steps.Where(s => s.UserId != null).Select(s => s.UserId!.Value).ToList();
        if (userIds.Count > 0 && await db.Users.CountAsync(u => userIds.Contains(u.Id), ct) != userIds.Distinct().Count())
        {
            return Error.Validation("USER_NOT_FOUND");
        }

        var roleCodes = r.Steps.Where(s => s.RoleCode != null).Select(s => s.RoleCode!.ToUpperInvariant()).Distinct().ToList();
        if (await db.Roles.CountAsync(x => roleCodes.Contains(x.Code), ct) != roleCodes.Count)
        {
            return Error.Validation("ROLE_NOT_FOUND");
        }

        template.SetSteps(r.Steps.Select(s => new WorkflowStepTemplate(
            s.Code, s.Name, s.ActionType, s.AssigneeType, s.RoleCode, s.UserId, s.AllowEdit, s.IsOptional, s.ReturnTo ?? [0], s.SignatureAnchor, s.SlaHours)));

        if (r.IsDefault)
        {
            // One default per document type.
            var others = await db.WorkflowTemplates.Where(t => t.DocumentType == r.DocumentType && t.IsDefault && t.Id != template.Id).ToListAsync(ct);
            others.ForEach(o => o.Update(o.Name, o.Condition, false, o.IsActive));
        }

        await db.SaveChangesAsync(ct);
        return template.Id;
    }

    // ---------------- Instances ----------------

    public async Task<Result<Guid>> StartAsync(StartWorkflowRequest r, CancellationToken ct)
    {
        var document = await db.Documents.Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == r.DocumentId, ct);
        if (document is null)
        {
            return Error.NotFound();
        }

        if (!current.Permissions.Overlaps(DocumentPermissions.Edit(document.Type, EditorMode.Edit)))
        {
            return Error.Forbidden();
        }

        if (await db.WorkflowInstances.AnyAsync(w => w.DocumentId == document.Id && (w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.Returned), ct))
        {
            return Error.Conflict("WORKFLOW_ALREADY_RUNNING");
        }

        var template = r.TemplateId is { } tid
            ? await db.WorkflowTemplates.Include("_steps").FirstOrDefaultAsync(t => t.Id == tid && t.IsActive, ct)
            : await db.WorkflowTemplates.Include("_steps").FirstOrDefaultAsync(t => t.DocumentType == document.Type && t.IsDefault && t.IsActive, ct);
        if (template is null)
        {
            return Error.Validation("WORKFLOW_TEMPLATE_NOT_FOUND");
        }

        if (template.DocumentType != document.Type && !current.HasPermission(Permissions.TemplateManage))
        {
            return Error.Validation("WORKFLOW_TEMPLATE_MISMATCH");
        }

        if (r.SupplierId is { } supplierId && document.SupplierId != supplierId)
        {
            if (document.Type != DocumentType.TemplateSample || !await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
            {
                return Error.Validation("SUPPLIER_NOT_FOUND");
            }

            document.SetSupplierParty(supplierId);
        }

        if (template.Steps.Any(s => s.AssigneeType == AssigneeType.DocumentSupplier) && document.SupplierId is null)
        {
            return Error.Validation("WORKFLOW_SUPPLIER_REQUIRED");
        }

        if (document.CurrentVersion is null)
        {
            return Error.Validation("DOCUMENT_DOCX_REQUIRED");
        }

        var wf = new WorkflowInstance(document.Id, template, current.UserId!.Value, current.FullName, document.SupplierId, clock.UtcNow);
        wf.SetScope(r.FacilityId, r.Domain);
        db.WorkflowInstances.Add(wf);
        document.MarkInWorkflow();
        audit.Log("WorkflowStart", nameof(Document), document.Id.ToString(), new { wf.Id, template.Code });

        // Everyone in the loop reads the PDF rendition; a signing step also needs the final (changes accepted) version.
        await documents.PrepareForSigningAsync(document, finalize: wf.ActiveStep?.RequiresSignature == true, ct);
        await PublishAsync(wf, document, WorkflowChangeKind.Started, null, null, ct);
        await NotifyActiveAsync(wf, document, ct);
        await db.SaveChangesAsync(ct);
        return wf.Id;
    }

    public async Task<Result<WorkflowDto?>> GetForDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return Error.NotFound();
        }

        var wf = await db.WorkflowInstances.AsNoTracking().Include("_steps").Include("_events")
            .Where(w => w.DocumentId == documentId).OrderByDescending(w => w.StartedAt).FirstOrDefaultAsync(ct);
        if (wf is null)
        {
            return Result.Success<WorkflowDto?>(null);
        }

        return await ToDtoAsync(wf, ct);
    }

    public async Task<Result<WorkflowDto>> ApproveAsync(Guid stepId, ApproveStepRequest r, CancellationToken ct)
    {
        var (wf, step, error) = await LoadActiveAsync(stepId, ct);
        if (error is not null)
        {
            return error;
        }

        if (step!.RequiresSignature)
        {
            return Error.Validation("WORKFLOW_SIGNATURE_REQUIRED");
        }

        var standing = await access.StandingAsync(wf!, step, ct);
        if (!standing.CanAct)
        {
            return step.ActedBy == current.UserId ? await ToDtoAsync(wf!, ct) : Error.Forbidden(standing.DenyReason ?? "FORBIDDEN");
        }

        var outcome = wf!.Act(step.Id, current.UserId!.Value, ActorName(standing), standing.OnBehalfOf, r.Comment, null, clock.UtcNow, r.SkipNextOptional);
        await AfterActAsync(wf, outcome, step, r.Comment, ct);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(wf, ct);
    }

    public async Task<Result<WorkflowDto>> ReturnAsync(Guid stepId, ReturnStepRequest r, CancellationToken ct)
    {
        var (wf, step, error) = await LoadActiveAsync(stepId, ct);
        if (error is not null)
        {
            return error;
        }

        var standing = await access.StandingAsync(wf!, step!, ct);
        if (!standing.CanAct)
        {
            return Error.Forbidden(standing.DenyReason ?? "FORBIDDEN");
        }

        wf!.Return(step!.Id, r.ToOrder, current.UserId!.Value, ActorName(standing), r.Reason, clock.UtcNow);
        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == wf.DocumentId, ct);
        audit.Log("WorkflowReturn", nameof(Document), document.Id.ToString(), new { stepId, r.ToOrder }, r.Reason);
        if (wf.Status == WorkflowStatus.Returned)
        {
            document.MarkDraft();
            await notifications.NotifyAsync([wf.StartedBy], NotificationTypes.WorkflowReturned, $"Tài liệu bị trả lại: {document.Title}",
                $"{ActorName(standing)}: {r.Reason}", $"/tai-lieu/{document.Id}", email: true, ct);
        }
        else
        {
            await PrepareActiveStepAsync(wf, document, ct);
            await NotifyActiveAsync(wf, document, ct, returnedReason: r.Reason);
        }

        await PublishAsync(wf, document, WorkflowChangeKind.Returned, step, r.Reason, ct);

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(wf, ct);
    }

    public async Task<Result<WorkflowDto>> CommentAsync(Guid instanceId, CommentRequest r, CancellationToken ct)
    {
        var wf = await db.WorkflowInstances.Include("_steps").Include("_events").FirstOrDefaultAsync(w => w.Id == instanceId, ct);
        if (wf is null || !await access.IsParticipantAsync(wf.DocumentId, ct))
        {
            return Error.NotFound();
        }

        wf.Comment(current.UserId!.Value, current.FullName ?? current.UserName ?? string.Empty, r.Text, clock.UtcNow);
        if (wf.ActiveStep is { } active)
        {
            var title = await db.Documents.Where(d => d.Id == wf.DocumentId).Select(d => d.Title).FirstAsync(ct);
            var targets = (await access.AssigneesAsync(wf, active, ct)).Append(wf.StartedBy).Where(id => id != current.UserId);
            await notifications.NotifyAsync(targets, NotificationTypes.WorkflowComment, $"Ý kiến mới: {title}", $"{current.FullName}: {r.Text}",
                $"/tai-lieu/{wf.DocumentId}", email: false, ct);
        }

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(wf, ct);
    }

    public async Task<Result<WorkflowDto>> ResubmitAsync(Guid instanceId, CancellationToken ct)
    {
        var wf = await db.WorkflowInstances.Include("_steps").Include("_events").FirstOrDefaultAsync(w => w.Id == instanceId, ct);
        if (wf is null)
        {
            return Error.NotFound();
        }

        if (wf.StartedBy != current.UserId)
        {
            return Error.Forbidden();
        }

        wf.Resubmit(current.UserId!.Value, current.FullName ?? string.Empty, clock.UtcNow);
        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == wf.DocumentId, ct);
        document.MarkInWorkflow();
        await PrepareActiveStepAsync(wf, document, ct);
        await PublishAsync(wf, document, WorkflowChangeKind.Resubmitted, null, null, ct);
        await NotifyActiveAsync(wf, document, ct);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(wf, ct);
    }

    public async Task<Result> CancelAsync(Guid instanceId, CommentRequest r, CancellationToken ct)
    {
        var wf = await db.WorkflowInstances.Include("_steps").Include("_events").FirstOrDefaultAsync(w => w.Id == instanceId, ct);
        if (wf is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (wf.StartedBy != current.UserId && !current.HasPermission(Permissions.WorkflowManage))
        {
            return Result.Failure(Error.Forbidden());
        }

        wf.Cancel(current.UserId!.Value, current.FullName ?? string.Empty, r.Text, clock.UtcNow);
        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == wf.DocumentId, ct);
        document.MarkDraft();
        audit.Log("WorkflowCancel", nameof(Document), document.Id.ToString(), new { wf.Id }, r.Text);
        await PublishAsync(wf, document, WorkflowChangeKind.Cancelled, null, r.Text, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------------- Inbox (Trình ký) ----------------

    public async Task<PagedResult<InboxItemDto>> InboxAsync(PageQuery q, InboxFilter f, CancellationToken ct)
    {
        var me = current.UserId!.Value;
        var now = clock.UtcNow;
        var instances = db.WorkflowInstances.AsNoTracking();
        IQueryable<InboxRow> rows;

        if (f.Tab == InboxTab.Pending)
        {
            var steps = await PendingStepsQueryAsync(ct);
            rows = from s in steps
                   join w in instances on s.InstanceId equals w.Id
                   select new InboxRow { Instance = w, Step = s };
        }
        else
        {
            var filtered = f.Tab switch
            {
                InboxTab.Processed => instances.Where(w => db.WorkflowEvents.Any(e => e.InstanceId == w.Id && e.ActorId == me &&
                    (e.Type == WorkflowEventType.Approved || e.Type == WorkflowEventType.SignatureAdded || e.Type == WorkflowEventType.Returned))),
                InboxTab.Sent => instances.Where(w => w.StartedBy == me),
                _ => instances.Where(w => w.StartedBy == me && w.Status == WorkflowStatus.Returned),
            };
            rows = from w in filtered
                   join s in db.WorkflowSteps.Where(x => x.Status == StepStatus.Active) on w.Id equals s.InstanceId into active
                   from s in active.DefaultIfEmpty()
                   select new InboxRow { Instance = w, Step = s };
        }

        var joined = from r in rows
                     join d in db.Documents.AsNoTracking() on r.Instance.DocumentId equals d.Id
                     select new { r.Instance, r.Step, Document = d };

        if (f.DocumentType is not null)
        {
            joined = joined.Where(x => x.Document.Type == f.DocumentType);
        }

        if (f.SupplierId is not null)
        {
            joined = joined.Where(x => x.Instance.SupplierId == f.SupplierId);
        }

        if (f.ContractId is not null)
        {
            joined = joined.Where(x => x.Document.ContractId == f.ContractId);
        }

        if (f.From is not null)
        {
            joined = joined.Where(x => x.Instance.StartedAt >= f.From);
        }

        if (f.To is not null)
        {
            joined = joined.Where(x => x.Instance.StartedAt < f.To);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            joined = joined.Where(x => EF.Functions.ILike(x.Document.Title, p));
        }

        // Order on a scalar before projecting into the DTO (EF cannot order by a constructor member).
        var projected = joined
            .Select(x => new
            {
                x.Instance,
                x.Step,
                x.Document,
                LastActivityAt = db.WorkflowEvents.Where(e => e.InstanceId == x.Instance.Id).Max(e => (DateTimeOffset?)e.At) ?? x.Instance.StartedAt,
            })
            .OrderByDescending(x => x.LastActivityAt)
            .Select(x => new InboxItemDto(
            x.Instance.Id,
            x.Document.Id,
            x.Document.Title,
            x.Document.Type,
            x.Instance.Status,
            x.Step == null ? null : x.Step.Id,
            x.Step == null ? null : x.Step.Name,
            x.Step == null ? null : x.Step.ActionType,
            x.Step != null && (x.Step.ActionType == WorkflowActionType.Sign || x.Step.ActionType == WorkflowActionType.Initial
                || x.Step.ActionType == WorkflowActionType.OrgSeal || x.Step.ActionType == WorkflowActionType.SupplierSign),
            x.Step == null ? null : x.Step.ActivatedAt,
            x.Step == null ? null : x.Step.DueAt,
            x.Step != null && x.Step.DueAt != null && x.Step.DueAt < now,
            x.Instance.StartedByName,
            x.Instance.StartedAt,
            db.Suppliers.Where(s => s.Id == x.Instance.SupplierId).Select(s => s.Name).FirstOrDefault(),
            x.LastActivityAt));

        return await projected.ToPagedAsync(q, ct);
    }

    public async Task<InboxCountDto> InboxCountAsync(CancellationToken ct)
    {
        var steps = await PendingStepsQueryAsync(ct);
        var now = clock.UtcNow;
        return new InboxCountDto(await steps.CountAsync(ct), await steps.CountAsync(s => s.DueAt != null && s.DueAt < now, ct));
    }

    /// <summary>Active steps the current user may act on (role/user/supplier/creator, data scope, delegation).</summary>
    private async Task<IQueryable<WorkflowStepInstance>> PendingStepsQueryAsync(CancellationToken ct)
    {
        var me = current.UserId!.Value;
        var roles = (await access.MyRolesAsync(ct)).ToList();
        roles.AddRange((await access.MyDelegationsAsync(ct)).SelectMany(d => d.Roles));
        var mySupplier = current.SupplierId;
        var scopes = current.DataScopes;
        var facilities = scopes.Where(s => s.Type == DataScopeType.Facility).Select(s => Guid.Parse(s.Value)).ToList();
        var domains = scopes.Where(s => s.Type == DataScopeType.Domain).Select(s => s.Value).ToList();
        var canSign = current.HasPermission(Permissions.SigningSign);

        return from s in db.WorkflowSteps.AsNoTracking()
               join w in db.WorkflowInstances.AsNoTracking() on s.InstanceId equals w.Id
               where canSign && s.Status == StepStatus.Active && w.Status == WorkflowStatus.Running
                     && ((s.AssigneeType == AssigneeType.Role && roles.Contains(s.RoleCode!)
                            && (w.ScopeFacilityId == null || facilities.Count == 0 || facilities.Contains(w.ScopeFacilityId.Value))
                            && (w.ScopeDomain == null || domains.Count == 0 || domains.Contains(w.ScopeDomain)))
                         || (s.AssigneeType == AssigneeType.User && s.AssigneeUserId == me)
                         || (s.AssigneeType == AssigneeType.Creator && w.StartedBy == me)
                         || (s.AssigneeType == AssigneeType.DocumentSupplier && mySupplier != null && w.SupplierId == mySupplier))
               select s;
    }

    // ---------------- Helpers shared with SigningService ----------------

    internal async Task<(WorkflowInstance? Workflow, WorkflowStepInstance? Step, Error? Error)> LoadActiveAsync(Guid stepId, CancellationToken ct)
    {
        var instanceId = await db.WorkflowSteps.Where(s => s.Id == stepId).Select(s => (Guid?)s.InstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is null)
        {
            return (null, null, Error.NotFound());
        }

        var wf = await db.WorkflowInstances.Include("_steps").Include("_events").FirstOrDefaultAsync(w => w.Id == instanceId, ct);
        if (wf is null)
        {
            return (null, null, Error.NotFound());
        }

        var step = wf.Steps.First(s => s.Id == stepId);
        if (step.Status == StepStatus.Done && step.ActedBy == current.UserId)
        {
            return (wf, step, null); // idempotent replay is handled by the domain
        }

        if (wf.Status != WorkflowStatus.Running || step.Status != StepStatus.Active)
        {
            return (wf, step, Error.Conflict("WORKFLOW_STEP_NOT_ACTIVE"));
        }

        return (wf, step, null);
    }

    internal async Task AfterActAsync(WorkflowInstance wf, ActOutcome outcome, WorkflowStepInstance actedStep, string? comment, CancellationToken ct)
    {
        if (outcome == ActOutcome.AlreadyDone)
        {
            return;
        }

        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == wf.DocumentId, ct);
        if (outcome == ActOutcome.Completed)
        {
            document.Complete();
            audit.Log("WorkflowCompleted", nameof(Document), document.Id.ToString(), new { wf.Id });
            await PublishAsync(wf, document, WorkflowChangeKind.Completed, actedStep, comment, ct);
            await notifications.NotifyAsync([wf.StartedBy], NotificationTypes.WorkflowCompleted, $"Đã hoàn tất ký duyệt: {document.Title}",
                null, $"/tai-lieu/{document.Id}", email: true, ct);
            return;
        }

        await PrepareActiveStepAsync(wf, document, ct);
        await PublishAsync(wf, document, WorkflowChangeKind.Advanced, actedStep, comment, ct);
        await NotifyActiveAsync(wf, document, ct);
    }

    /// <summary>Before a signing step: "chốt bản" (accept tracked changes) and render the PDF to sign.</summary>
    private Task PrepareActiveStepAsync(WorkflowInstance wf, Document document, CancellationToken ct) =>
        wf.ActiveStep?.RequiresSignature == true ? documents.PrepareForSigningAsync(document, finalize: true, ct) : Task.CompletedTask;

    internal async Task PublishAsync(WorkflowInstance wf, Document document, WorkflowChangeKind kind, WorkflowStepInstance? step, string? note, CancellationToken ct)
    {
        foreach (var observer in observers.Where(o => o.Handles(document.Type)))
        {
            await observer.OnChangedAsync(new WorkflowChange(wf, document, kind, step, note), ct);
        }
    }

    private string ActorName(ActorStanding standing) =>
        standing.Caption is null ? current.FullName ?? current.UserName ?? string.Empty : $"{current.FullName} ({standing.Caption})";

    private async Task NotifyActiveAsync(WorkflowInstance wf, Document document, CancellationToken ct, string? returnedReason = null)
    {
        if (wf.ActiveStep is not { } step)
        {
            return;
        }

        var assignees = await access.AssigneesAsync(wf, step, ct);
        var title = returnedReason is null ? $"Chờ bạn xử lý: {document.Title}" : $"Tài liệu được trả lại cho bạn: {document.Title}";
        var body = returnedReason is null ? $"Bước: {step.Name}" : $"Bước: {step.Name}. Lý do: {returnedReason}";
        await notifications.NotifyAsync(assignees, returnedReason is null ? NotificationTypes.SigningRequired : NotificationTypes.WorkflowReturned,
            title, body, $"/trinh-ky?document={document.Id}", email: true, ct);
    }

    private async Task<WorkflowDto> ToDtoAsync(WorkflowInstance wf, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var active = wf.ActiveStep;
        ActorStanding? standing = active is null ? null : await access.StandingAsync(wf, active, ct);
        var roleNames = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Name, ct);
        var userIds = wf.Steps.Where(s => s.AssigneeUserId != null).Select(s => s.AssigneeUserId!.Value).ToList();
        var userNames = await db.Users.IgnoreQueryFilters().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var supplierName = wf.SupplierId is { } sid
            ? await db.Suppliers.IgnoreQueryFilters().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;
        var signatures = await db.Signatures.AsNoTracking().Where(s => s.DocumentId == wf.DocumentId).OrderBy(s => s.SignedAt)
            .Select(s => new SignatureDto(s.Id, s.DocumentVersionId, s.SignerName, s.SignerTitle, s.Caption, s.Kind, s.Provider,
                s.CertificateSubject, s.CertificateSerial, s.SignedAt, s.HashAfter))
            .ToListAsync(ct);

        string Assignee(WorkflowStepInstance s) => s.AssigneeType switch
        {
            AssigneeType.Role => roleNames.GetValueOrDefault(s.RoleCode!, s.RoleCode!),
            AssigneeType.User => userNames.GetValueOrDefault(s.AssigneeUserId!.Value, "Người dùng"),
            AssigneeType.DocumentSupplier => supplierName ?? "Nhà cung cấp",
            _ => wf.StartedByName ?? "Người lập",
        };

        var steps = wf.Steps.Select(s => new WorkflowStepDto(
            s.Id, s.Order, s.Code, s.Name, s.ActionType, s.AssigneeType, Assignee(s), s.Status, s.ActedByName, s.ActedAt, s.Comment, s.ActivatedAt, s.DueAt,
            s.Status == StepStatus.Active && s.DueAt < now, s.RequiresSignature, s.IsOptional, s.AllowEdit, s.SignatureId)).ToList();
        var events = wf.Events.Where(e => e.Type != WorkflowEventType.StepActivated)
            .Select(e => new WorkflowEventDto(e.Type, e.StepName, e.ActorName, e.Note, e.TargetOrder, e.At)).ToList();
        var returnTargets = standing?.CanAct == true
            ? active!.ReturnToOrders.Select(o => new ReturnTargetDto(o, o == 0 ? "Người lập (trả về bản nháp)" : wf.Steps.First(s => s.Order == o).Name)).ToList()
            : [];

        return new WorkflowDto(
            wf.Id, wf.DocumentId, wf.TemplateCode, wf.Status, wf.StartedByName, wf.StartedAt, wf.CompletedAt, steps, events,
            standing?.CanAct == true ? active!.Id : null, standing?.Caption, returnTargets,
            wf.Status == WorkflowStatus.Returned && wf.StartedBy == current.UserId,
            wf.Status is WorkflowStatus.Running or WorkflowStatus.Returned && (wf.StartedBy == current.UserId || current.HasPermission(Permissions.WorkflowManage)),
            signatures);
    }

    private static WorkflowTemplateDto ToDto(WorkflowTemplate t, int running, Dictionary<Guid, string> names) => new(
        t.Id, t.Code, t.Name, t.DocumentType, t.Condition, t.IsDefault, t.IsActive,
        t.Steps.Select(s => new WorkflowStepTemplateDto(s.Code, s.Name, s.ActionType, s.AssigneeType, s.RoleCode, s.UserId,
            s.UserId is { } u ? names.GetValueOrDefault(u) : null, s.AllowEdit, s.IsOptional, s.ReturnToOrders, s.SignatureAnchor, s.SlaHours)).ToList(),
        running);

    private sealed class InboxRow
    {
        public required WorkflowInstance Instance { get; init; }

        public WorkflowStepInstance? Step { get; init; }
    }
}
