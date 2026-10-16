using BlueContract.Domain.Common;
using BlueContract.Domain.Documents;

namespace BlueContract.Domain.Workflows;

/// <summary>What a workflow step asks the assignee to do (spec §5.7).</summary>
public enum WorkflowActionType
{
    /// <summary>Rà soát — check and pass on (may edit when AllowEdit).</summary>
    Review = 0,

    /// <summary>Duyệt — approve without a digital signature.</summary>
    Approve = 1,

    /// <summary>Ký số cá nhân (PAdES).</summary>
    Sign = 2,

    /// <summary>Ký nháy — internal initial, small appearance.</summary>
    Initial = 3,

    /// <summary>Đóng dấu điện tử — organisation certificate + seal image (CLERK only).</summary>
    OrgSeal = 4,

    /// <summary>NCC rà soát (agree / send back with tracked changes).</summary>
    SupplierReview = 5,

    /// <summary>NCC ký số.</summary>
    SupplierSign = 6,

    /// <summary>Xác nhận (no signature).</summary>
    Confirm = 7,
}

/// <summary>How the step's actor is determined.</summary>
public enum AssigneeType
{
    /// <summary>Any user holding <see cref="WorkflowStepTemplate.RoleCode"/> (respecting data scope).</summary>
    Role = 0,

    /// <summary>A specific user.</summary>
    User = 1,

    /// <summary>Users of the supplier party of the document.</summary>
    DocumentSupplier = 2,

    /// <summary>The user who started the workflow.</summary>
    Creator = 3,
}

/// <summary>Cấu hình luồng duyệt/ký for a document type (+ optional conditions).</summary>
public sealed class WorkflowTemplate : Entity
{
    private readonly List<WorkflowStepTemplate> _steps = [];

    private WorkflowTemplate()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public WorkflowTemplate(string code, string name, DocumentType documentType, string? condition, bool isDefault)
    {
        Code = Guard.Required(code, "WORKFLOW_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "WORKFLOW_NAME_REQUIRED");
        DocumentType = documentType;
        Condition = Guard.Optional(condition);
        IsDefault = isDefault;
        IsActive = true;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public DocumentType DocumentType { get; private set; }

    /// <summary>Optional selector such as "ContractKind=LumpSum" or "Domain=Drug" (evaluated by the module starting the workflow).</summary>
    public string? Condition { get; private set; }

    /// <summary>Used when no conditional template matches.</summary>
    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<WorkflowStepTemplate> Steps => _steps.OrderBy(s => s.Order).ToList();

    public void Update(string name, string? condition, bool isDefault, bool isActive)
    {
        Name = Guard.Required(name, "WORKFLOW_NAME_REQUIRED");
        Condition = Guard.Optional(condition);
        IsDefault = isDefault;
        IsActive = isActive;
    }

    /// <summary>Replaces all steps (the admin UI saves the whole ordered list). Running instances keep their own copy.</summary>
    public void SetSteps(IEnumerable<WorkflowStepTemplate> steps)
    {
        var list = steps.ToList();
        if (list.Count == 0)
        {
            throw new DomainException("WORKFLOW_STEPS_REQUIRED");
        }

        var codes = list.Select(s => s.Code).ToList();
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
        {
            throw new DomainException("WORKFLOW_STEP_CODE_DUPLICATE");
        }

        for (var i = 0; i < list.Count; i++)
        {
            list[i].AttachTo(Id, i + 1);
            if (list[i].ReturnToOrders.Any(o => o >= i + 1 || o < 0))
            {
                throw new DomainException("WORKFLOW_RETURN_TARGET_INVALID");
            }
        }

        _steps.Clear();
        _steps.AddRange(list);
    }
}

public sealed class WorkflowStepTemplate
{
    private WorkflowStepTemplate()
    {
        Code = string.Empty;
        Name = string.Empty;
        ReturnTo = string.Empty;
    }

    public WorkflowStepTemplate(
        string code,
        string name,
        WorkflowActionType actionType,
        AssigneeType assigneeType,
        string? roleCode,
        Guid? userId,
        bool allowEdit,
        bool isOptional,
        IEnumerable<int> returnToOrders,
        string? signatureAnchor,
        int? slaHours)
    {
        Id = Guid.CreateVersion7();
        Code = Guard.Required(code, "WORKFLOW_STEP_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "WORKFLOW_STEP_NAME_REQUIRED");
        ActionType = actionType;
        AssigneeType = assigneeType;
        RoleCode = assigneeType == AssigneeType.Role ? Guard.Required(roleCode, "WORKFLOW_STEP_ROLE_REQUIRED").ToUpperInvariant() : null;
        UserId = assigneeType == AssigneeType.User ? userId ?? throw new DomainException("WORKFLOW_STEP_USER_REQUIRED") : null;
        AllowEdit = allowEdit;
        IsOptional = isOptional;
        ReturnTo = string.Join(',', returnToOrders.Distinct().Order());
        SignatureAnchor = Guard.Optional(signatureAnchor);
        SlaHours = slaHours;

        if (RequiresSignature && SignatureAnchor is null)
        {
            SignatureAnchor = "SIG_" + Code;
        }

        if (actionType is WorkflowActionType.SupplierReview or WorkflowActionType.SupplierSign && assigneeType != AssigneeType.DocumentSupplier)
        {
            throw new DomainException("WORKFLOW_SUPPLIER_STEP_ASSIGNEE");
        }
    }

    public Guid Id { get; private set; }

    public Guid TemplateId { get; private set; }

    public int Order { get; private set; }

    /// <summary>Stable code, e.g. DIRECTOR, STOREKEEPER; also the default signature anchor suffix.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public WorkflowActionType ActionType { get; private set; }

    public AssigneeType AssigneeType { get; private set; }

    public string? RoleCode { get; private set; }

    public Guid? UserId { get; private set; }

    /// <summary>The assignee may edit the document content at this step.</summary>
    public bool AllowEdit { get; private set; }

    /// <summary>May be skipped by the module (e.g. accountant review only when the supplier changed the contract).</summary>
    public bool IsOptional { get; private set; }

    /// <summary>Comma-separated step orders this step may return to; "0" = back to the creator (draft).</summary>
    public string ReturnTo { get; private set; }

    public IReadOnlyList<int> ReturnToOrders =>
        ReturnTo.Length == 0 ? [] : ReturnTo.Split(',').Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    /// <summary>Text anchor such as SIG_DIRECTOR placed in the template where the signature appears.</summary>
    public string? SignatureAnchor { get; private set; }

    public int? SlaHours { get; private set; }

    public bool RequiresSignature => ActionType is WorkflowActionType.Sign or WorkflowActionType.Initial or WorkflowActionType.OrgSeal or WorkflowActionType.SupplierSign;

    internal void AttachTo(Guid templateId, int order)
    {
        TemplateId = templateId;
        Order = order;
    }
}
