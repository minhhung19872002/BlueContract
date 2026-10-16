using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using FluentValidation;

namespace BlueContract.Application.Workflows;

public sealed record WorkflowStepTemplateDto(
    string Code,
    string Name,
    WorkflowActionType ActionType,
    AssigneeType AssigneeType,
    string? RoleCode,
    Guid? UserId,
    string? UserName,
    bool AllowEdit,
    bool IsOptional,
    IReadOnlyList<int> ReturnTo,
    string? SignatureAnchor,
    int? SlaHours);

public sealed record WorkflowTemplateDto(
    Guid Id,
    string Code,
    string Name,
    DocumentType DocumentType,
    string? Condition,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<WorkflowStepTemplateDto> Steps,
    int RunningCount);

public sealed record SaveWorkflowStepRequest(
    string Code,
    string Name,
    WorkflowActionType ActionType,
    AssigneeType AssigneeType,
    string? RoleCode,
    Guid? UserId,
    bool AllowEdit,
    bool IsOptional,
    IReadOnlyList<int>? ReturnTo,
    string? SignatureAnchor,
    int? SlaHours);

public sealed record SaveWorkflowTemplateRequest(
    string Code,
    string Name,
    DocumentType DocumentType,
    string? Condition,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<SaveWorkflowStepRequest> Steps);

public sealed record StartWorkflowRequest(Guid DocumentId, Guid? TemplateId, Guid? SupplierId, Guid? FacilityId, string? Domain);

public sealed record ApproveStepRequest(string? Comment, bool SkipNextOptional);

public sealed record ReturnStepRequest(int ToOrder, string Reason);

public sealed record CommentRequest(string Text);

public sealed record WorkflowStepDto(
    Guid Id,
    int Order,
    string Code,
    string Name,
    WorkflowActionType ActionType,
    AssigneeType AssigneeType,
    string AssigneeDisplay,
    StepStatus Status,
    string? ActedByName,
    DateTimeOffset? ActedAt,
    string? Comment,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    bool RequiresSignature,
    bool IsOptional,
    bool AllowEdit,
    Guid? SignatureId);

public sealed record WorkflowEventDto(WorkflowEventType Type, string? StepName, string? ActorName, string? Note, int? TargetOrder, DateTimeOffset At);

public sealed record ReturnTargetDto(int Order, string Name);

public sealed record SignatureDto(
    Guid Id,
    Guid DocumentVersionId,
    string SignerName,
    string? SignerTitle,
    string? Caption,
    SignatureKind Kind,
    string Provider,
    string? CertificateSubject,
    string? CertificateSerial,
    DateTimeOffset SignedAt,
    string HashAfter);

public sealed record WorkflowDto(
    Guid InstanceId,
    Guid DocumentId,
    string TemplateCode,
    WorkflowStatus Status,
    string? StartedByName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<WorkflowStepDto> Steps,
    IReadOnlyList<WorkflowEventDto> Events,
    Guid? MyStepId,
    string? MyCaption,
    IReadOnlyList<ReturnTargetDto> ReturnTargets,
    bool CanResubmit,
    bool CanCancel,
    IReadOnlyList<SignatureDto> Signatures,
    WorkflowGateDto? Gate);

/// <summary>Business precondition on the caller's active step (e.g. receipt validation): blocked, or a reason is required.</summary>
public sealed record WorkflowGateDto(bool Blocked, bool ReasonRequired, string Code, string Message);

public enum InboxTab
{
    /// <summary>Chờ tôi xử lý.</summary>
    Pending = 0,

    /// <summary>Tôi đã xử lý.</summary>
    Processed = 1,

    /// <summary>Tôi đã gửi.</summary>
    Sent = 2,

    /// <summary>Bị trả lại.</summary>
    Returned = 3,
}

/// <summary>
/// Inbox filters. <c>ActionType</c> keeps only steps of this kind — the "Rà soát" menu asks for
/// <see cref="WorkflowActionType.Review"/>. Pending: the step waiting for me; Processed: a step of this kind I acted on;
/// Sent / Returned: a document whose workflow has such a step.
/// </summary>
public sealed record InboxFilter(
    InboxTab Tab,
    DocumentType? DocumentType,
    Guid? SupplierId,
    Guid? ContractId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    WorkflowActionType? ActionType = null);

public sealed record InboxItemDto(
    Guid InstanceId,
    Guid DocumentId,
    string DocumentTitle,
    DocumentType DocumentType,
    WorkflowStatus WorkflowStatus,
    Guid? StepId,
    string? StepName,
    WorkflowActionType? ActionType,
    bool RequiresSignature,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    string? StartedByName,
    DateTimeOffset StartedAt,
    string? SupplierName,
    DateTimeOffset LastActivityAt);

public sealed record InboxCountDto(int Pending, int Overdue);

internal sealed class SaveWorkflowTemplateRequestValidator : AbstractValidator<SaveWorkflowTemplateRequest>
{
    public SaveWorkflowTemplateRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("Mã luồng").Matches("^[A-Za-z0-9_]{2,60}$").WithMessage("Mã luồng chỉ gồm chữ không dấu, số, gạch dưới.");
        RuleFor(x => x.Name).NotEmpty().WithName("Tên luồng").MaximumLength(300);
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.Steps).NotEmpty().WithMessage("Luồng phải có ít nhất một bước.");
        RuleForEach(x => x.Steps).ChildRules(s =>
        {
            s.RuleFor(x => x.Code).NotEmpty().WithName("Mã bước").Matches("^[A-Za-z0-9_]{2,60}$").WithMessage("Mã bước chỉ gồm chữ không dấu, số, gạch dưới.");
            s.RuleFor(x => x.Name).NotEmpty().WithName("Tên bước");
            s.RuleFor(x => x.RoleCode).NotEmpty().When(x => x.AssigneeType == AssigneeType.Role).WithName("Vai trò");
            s.RuleFor(x => x.UserId).NotEmpty().When(x => x.AssigneeType == AssigneeType.User).WithName("Người thực hiện");
            s.RuleFor(x => x.SlaHours).GreaterThan(0).When(x => x.SlaHours is not null).WithName("SLA (giờ)");
        });
    }
}

internal sealed class ReturnStepRequestValidator : AbstractValidator<ReturnStepRequest>
{
    public ReturnStepRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithName("Lý do trả lại").MaximumLength(2000);
        RuleFor(x => x.ToOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CommentRequestValidator : AbstractValidator<CommentRequest>
{
    public CommentRequestValidator() => RuleFor(x => x.Text).NotEmpty().WithName("Ý kiến").MaximumLength(2000);
}
