using BlueContract.Domain.Common;

namespace BlueContract.Domain.Workflows;

public enum WorkflowStatus
{
    Running = 0,
    Completed = 1,

    /// <summary>Returned to the creator (step 0) — the document is a draft again.</summary>
    Returned = 2,
    Cancelled = 3,
}

public enum StepStatus
{
    Pending = 0,
    Active = 1,
    Done = 2,
    Skipped = 3,
}

/// <summary>Result of acting on a step; repeated identical requests are no-ops (idempotent).</summary>
public enum ActOutcome
{
    Advanced = 0,
    Completed = 1,
    AlreadyDone = 2,
}

/// <summary>
/// A running signing/approval workflow over one document — the state machine at the heart of the system (spec §5.7).
/// Steps are copied from the template at start so later template edits never affect documents in flight.
/// Concurrency: the instance row carries xmin; two people acting on the same step → the second save conflicts.
/// </summary>
public sealed class WorkflowInstance : Entity
{
    private readonly List<WorkflowStepInstance> _steps = [];
    private readonly List<WorkflowEvent> _events = [];

    private WorkflowInstance()
    {
        TemplateCode = string.Empty;
    }

    public WorkflowInstance(Guid documentId, WorkflowTemplate template, Guid startedBy, string? startedByName, Guid? supplierId, DateTimeOffset now)
    {
        if (!template.IsActive)
        {
            throw new DomainException("WORKFLOW_TEMPLATE_INACTIVE");
        }

        DocumentId = documentId;
        TemplateId = template.Id;
        TemplateCode = template.Code;
        StartedBy = startedBy;
        StartedByName = startedByName;
        SupplierId = supplierId;
        StartedAt = now;
        Status = WorkflowStatus.Running;
        foreach (var s in template.Steps)
        {
            _steps.Add(new WorkflowStepInstance(Id, s));
        }

        if (_steps.Count == 0)
        {
            throw new DomainException("WORKFLOW_STEPS_REQUIRED");
        }

        AddEvent(WorkflowEventType.Started, null, startedBy, startedByName, null, now);
        Activate(_steps[0], now);
    }

    public Guid DocumentId { get; private set; }

    public Guid TemplateId { get; private set; }

    public string TemplateCode { get; private set; }

    public WorkflowStatus Status { get; private set; }

    public Guid StartedBy { get; private set; }

    public string? StartedByName { get; private set; }

    /// <summary>Supplier party (for DocumentSupplier steps and supplier isolation of the inbox).</summary>
    public Guid? SupplierId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Facility of the document — role assignees with facility data scope only see matching workflows.</summary>
    public Guid? ScopeFacilityId { get; private set; }

    /// <summary>Procurement domain (Drug, MedicalSupply...) — e.g. the pharmacy head only sees drug summaries.</summary>
    public string? ScopeDomain { get; private set; }

    public void SetScope(Guid? facilityId, string? domain)
    {
        ScopeFacilityId = facilityId;
        ScopeDomain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim();
    }

    public IReadOnlyList<WorkflowStepInstance> Steps => _steps.OrderBy(s => s.Order).ToList();

    public IReadOnlyList<WorkflowEvent> Events => _events.OrderBy(e => e.At).ThenBy(e => e.Sequence).ToList();

    public WorkflowStepInstance? ActiveStep => _steps.FirstOrDefault(s => s.Status == StepStatus.Active);

    /// <summary>Completes the active step (approve / sign / confirm...). Idempotent for the same actor.</summary>
    public ActOutcome Act(Guid stepId, Guid actorId, string actorName, Guid? onBehalfOf, string? comment, Guid? signatureId, DateTimeOffset now, bool skipNextOptional = false)
    {
        var step = Find(stepId);
        if (step.Status == StepStatus.Done && step.ActedBy == actorId)
        {
            return ActOutcome.AlreadyDone;
        }

        EnsureRunning();
        if (step.Status != StepStatus.Active)
        {
            throw new DomainException("WORKFLOW_STEP_NOT_ACTIVE");
        }

        if (step.RequiresSignature && signatureId is null)
        {
            throw new DomainException("WORKFLOW_SIGNATURE_REQUIRED");
        }

        step.Complete(actorId, actorName, onBehalfOf, comment, signatureId, now);
        AddEvent(step.RequiresSignature ? WorkflowEventType.SignatureAdded : WorkflowEventType.Approved, step, actorId, actorName, comment, now);

        var next = _steps.Where(s => s.Order > step.Order && s.Status is StepStatus.Pending).OrderBy(s => s.Order).ToList();
        while (skipNextOptional && next.Count > 0 && next[0].IsOptional)
        {
            next[0].Skip(now);
            AddEvent(WorkflowEventType.Skipped, next[0], actorId, actorName, null, now);
            next.RemoveAt(0);
            skipNextOptional = false;
        }

        if (next.Count == 0)
        {
            Status = WorkflowStatus.Completed;
            CompletedAt = now;
            AddEvent(WorkflowEventType.Completed, null, actorId, actorName, null, now);
            return ActOutcome.Completed;
        }

        Activate(next[0], now);
        return ActOutcome.Advanced;
    }

    /// <summary>
    /// Returns the document from the active step to an earlier step allowed by configuration (or 0 = creator).
    /// Every step from the target up to the current one must be redone.
    /// </summary>
    public void Return(Guid stepId, int toOrder, Guid actorId, string actorName, string reason, DateTimeOffset now)
    {
        EnsureRunning();
        var step = Find(stepId);
        if (step.Status != StepStatus.Active)
        {
            throw new DomainException("WORKFLOW_STEP_NOT_ACTIVE");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("WORKFLOW_RETURN_REASON_REQUIRED");
        }

        if (!step.ReturnToOrders.Contains(toOrder))
        {
            throw new DomainException("WORKFLOW_RETURN_TARGET_NOT_ALLOWED");
        }

        step.MarkReturned(actorId, actorName, reason, now);
        AddEvent(WorkflowEventType.Returned, step, actorId, actorName, reason, now, toOrder);
        foreach (var s in _steps.Where(s => s.Order >= Math.Max(toOrder, 1) && s.Order <= step.Order))
        {
            s.Reset();
        }

        if (toOrder == 0)
        {
            Status = WorkflowStatus.Returned;
            return;
        }

        Activate(_steps.Single(s => s.Order == toOrder), now);
    }

    /// <summary>The creator resubmits a returned workflow; it restarts from the first step.</summary>
    public void Resubmit(Guid actorId, string actorName, DateTimeOffset now)
    {
        if (Status != WorkflowStatus.Returned)
        {
            throw new DomainException("WORKFLOW_NOT_RETURNED");
        }

        foreach (var s in _steps)
        {
            s.Reset();
        }

        Status = WorkflowStatus.Running;
        AddEvent(WorkflowEventType.Resubmitted, null, actorId, actorName, null, now);
        Activate(_steps.OrderBy(s => s.Order).First(), now);
    }

    /// <summary>A new document version after signatures invalidates them: every signing step must be redone.</summary>
    public void RestartForNewVersion(Guid actorId, string actorName, DateTimeOffset now)
    {
        if (Status is WorkflowStatus.Completed or WorkflowStatus.Cancelled)
        {
            throw new DomainException("WORKFLOW_CLOSED");
        }

        foreach (var s in _steps)
        {
            s.Reset();
        }

        Status = WorkflowStatus.Running;
        AddEvent(WorkflowEventType.Restarted, null, actorId, actorName, "Tài liệu có phiên bản mới — ký lại từ đầu.", now);
        Activate(_steps.OrderBy(s => s.Order).First(), now);
    }

    /// <summary>"Chuyển tiếp ý kiến": a comment on the active step without acting.</summary>
    public void Comment(Guid actorId, string actorName, string text, DateTimeOffset now)
    {
        EnsureNotClosed();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainException("WORKFLOW_COMMENT_REQUIRED");
        }

        AddEvent(WorkflowEventType.Commented, ActiveStep, actorId, actorName, text.Trim(), now);
    }

    public void Cancel(Guid actorId, string actorName, string reason, DateTimeOffset now)
    {
        EnsureNotClosed();
        Status = WorkflowStatus.Cancelled;
        AddEvent(WorkflowEventType.Cancelled, ActiveStep, actorId, actorName, reason, now);
    }

    private void Activate(WorkflowStepInstance step, DateTimeOffset now)
    {
        step.Activate(now);
        AddEvent(WorkflowEventType.StepActivated, step, null, null, null, now);
    }

    private WorkflowStepInstance Find(Guid stepId) =>
        _steps.FirstOrDefault(s => s.Id == stepId) ?? throw new DomainException("WORKFLOW_STEP_NOT_FOUND");

    private void EnsureRunning()
    {
        if (Status != WorkflowStatus.Running)
        {
            throw new DomainException(Status == WorkflowStatus.Completed ? "WORKFLOW_COMPLETED" : "WORKFLOW_NOT_RUNNING");
        }
    }

    private void EnsureNotClosed()
    {
        if (Status is WorkflowStatus.Completed or WorkflowStatus.Cancelled)
        {
            throw new DomainException("WORKFLOW_CLOSED");
        }
    }

    private void AddEvent(WorkflowEventType type, WorkflowStepInstance? step, Guid? actorId, string? actorName, string? note, DateTimeOffset at, int? targetOrder = null) =>
        _events.Add(new WorkflowEvent(Id, _events.Count + 1, type, step?.Id, step?.Name, actorId, actorName, note, targetOrder, at));
}

public sealed class WorkflowStepInstance
{
    private WorkflowStepInstance()
    {
        Code = string.Empty;
        Name = string.Empty;
        ReturnTo = string.Empty;
    }

    internal WorkflowStepInstance(Guid instanceId, WorkflowStepTemplate t)
    {
        Id = Guid.CreateVersion7();
        InstanceId = instanceId;
        Order = t.Order;
        Code = t.Code;
        Name = t.Name;
        ActionType = t.ActionType;
        AssigneeType = t.AssigneeType;
        RoleCode = t.RoleCode;
        AssigneeUserId = t.UserId;
        AllowEdit = t.AllowEdit;
        IsOptional = t.IsOptional;
        ReturnTo = t.ReturnTo;
        SignatureAnchor = t.SignatureAnchor;
        SlaHours = t.SlaHours;
        Status = StepStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid InstanceId { get; private set; }

    public int Order { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public WorkflowActionType ActionType { get; private set; }

    public AssigneeType AssigneeType { get; private set; }

    public string? RoleCode { get; private set; }

    public Guid? AssigneeUserId { get; private set; }

    public bool AllowEdit { get; private set; }

    public bool IsOptional { get; private set; }

    public string ReturnTo { get; private set; }

    public IReadOnlyList<int> ReturnToOrders =>
        ReturnTo.Length == 0 ? [] : ReturnTo.Split(',').Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    public string? SignatureAnchor { get; private set; }

    public int? SlaHours { get; private set; }

    public StepStatus Status { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public Guid? ActedBy { get; private set; }

    public string? ActedByName { get; private set; }

    /// <summary>When signing under delegation: the delegator ("KT. GIÁM ĐỐC").</summary>
    public Guid? OnBehalfOfUserId { get; private set; }

    public DateTimeOffset? ActedAt { get; private set; }

    public string? Comment { get; private set; }

    public Guid? SignatureId { get; private set; }

    /// <summary>Last time an SLA reminder was sent for this activation.</summary>
    public DateTimeOffset? ReminderSentAt { get; private set; }

    /// <summary>PostgreSQL xmin: two people acting on the same step at once → the second save fails (409).</summary>
    public uint RowVersion { get; private set; }

    public bool RequiresSignature => ActionType is WorkflowActionType.Sign or WorkflowActionType.Initial or WorkflowActionType.OrgSeal or WorkflowActionType.SupplierSign;

    public bool IsSupplierStep => AssigneeType == AssigneeType.DocumentSupplier;

    public void MarkReminderSent(DateTimeOffset at) => ReminderSentAt = at;

    internal void Activate(DateTimeOffset now)
    {
        Status = StepStatus.Active;
        ActivatedAt = now;
        DueAt = SlaHours is { } h ? now.AddHours(h) : null;
        ReminderSentAt = null;
    }

    internal void Complete(Guid actor, string actorName, Guid? onBehalfOf, string? comment, Guid? signatureId, DateTimeOffset now)
    {
        Status = StepStatus.Done;
        ActedBy = actor;
        ActedByName = actorName;
        OnBehalfOfUserId = onBehalfOf;
        ActedAt = now;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        SignatureId = signatureId;
    }

    internal void MarkReturned(Guid actor, string actorName, string reason, DateTimeOffset now)
    {
        ActedBy = actor;
        ActedByName = actorName;
        ActedAt = now;
        Comment = reason.Trim();
    }

    internal void Skip(DateTimeOffset now)
    {
        Status = StepStatus.Skipped;
        ActedAt = now;
    }

    internal void Reset()
    {
        Status = StepStatus.Pending;
        ActivatedAt = null;
        DueAt = null;
        ActedBy = null;
        ActedByName = null;
        OnBehalfOfUserId = null;
        ActedAt = null;
        SignatureId = null;
        ReminderSentAt = null;
    }
}

public enum WorkflowEventType
{
    Started = 0,
    StepActivated = 1,
    Approved = 2,
    SignatureAdded = 3,
    Returned = 4,
    Skipped = 5,
    Commented = 6,
    Completed = 7,
    Resubmitted = 8,
    Restarted = 9,
    Cancelled = 10,
}

/// <summary>Immutable history line shown in the workflow timeline ("lịch sử luồng + ý kiến các bước").</summary>
public sealed class WorkflowEvent
{
    private WorkflowEvent()
    {
    }

    internal WorkflowEvent(Guid instanceId, int sequence, WorkflowEventType type, Guid? stepId, string? stepName, Guid? actorId, string? actorName, string? note, int? targetOrder, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        InstanceId = instanceId;
        Sequence = sequence;
        Type = type;
        StepId = stepId;
        StepName = stepName;
        ActorId = actorId;
        ActorName = actorName;
        Note = note;
        TargetOrder = targetOrder;
        At = at;
    }

    public Guid Id { get; private set; }

    public Guid InstanceId { get; private set; }

    public int Sequence { get; private set; }

    public WorkflowEventType Type { get; private set; }

    public Guid? StepId { get; private set; }

    public string? StepName { get; private set; }

    public Guid? ActorId { get; private set; }

    public string? ActorName { get; private set; }

    public string? Note { get; private set; }

    public int? TargetOrder { get; private set; }

    public DateTimeOffset At { get; private set; }
}
