using BlueContract.Application.Admin;
using BlueContract.Application.Documents;
using BlueContract.Domain.Contracts;
using FluentValidation;

namespace BlueContract.Application.Contracts;

public sealed record ContractListItemDto(
    Guid Id,
    string Number,
    string Name,
    ProcurementType ProcurementType,
    ContractKind Kind,
    Guid SupplierId,
    string SupplierName,
    Guid CategoryId,
    string CategoryName,
    string? BidDecisionNumber,
    ContractStatus Status,
    decimal TotalAmount,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    decimal ReceivedPercent,
    bool RequiresWarehouseReceipt,
    DateTimeOffset CreatedAt);

public sealed record ContractFilter(
    Guid? SupplierId,
    Guid? CategoryId,
    ContractStatus? Status,
    ProcurementType? ProcurementType,
    Guid? BidDecisionId,
    bool? EffectiveOnly,
    bool? ExpiringSoon);

public sealed record ContractItemDto(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string Unit,
    string? HisKey,
    string? Specification,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal Amount,
    decimal VatAmount,
    decimal? MaxUnitPrice,
    decimal ProposedQuantity,
    decimal ApprovedQuantity,
    decimal CalledQuantity,
    decimal ReceivedQuantity,
    decimal AcceptedQuantity,
    decimal PaidQuantity,
    decimal RemainingQuantity);

public sealed record ContractAppendixDto(
    Guid Id,
    string Number,
    AppendixType Type,
    string Reason,
    string? InfoChanges,
    DateOnly? OldExpiryDate,
    DateOnly? NewExpiryDate,
    decimal ValueChange,
    AppendixStatus Status,
    Guid? DocumentId,
    DateTimeOffset? AppliedAt,
    IReadOnlyList<AppendixLineDto> Lines,
    IReadOnlyList<AppendixInfoChange> InfoFields);

public sealed record AppendixLineDto(Guid ContractItemId, string ItemName, decimal OldQuantity, decimal? NewQuantity, decimal OldUnitPrice, decimal? NewUnitPrice, decimal ValueChange);

/// <summary>What the caller may do on the contract right now (drives the buttons of the detail screen).</summary>
public sealed record ContractActionsDto(
    bool CanEdit,
    bool CanSubmit,
    bool CanCancel,
    bool CanCreateAppendix,
    Guid? SupplierReviewStepId,
    Guid? AccountantReviewStepId,
    int? SupplierReviewOrder,
    int PendingRevisionCount);

/// <summary>
/// One line of the contract "Lịch sử" tab (spec §9.1-5): an audit entry of the contract, one of its documents or
/// appendices. <c>Subject</c> is the audited type (Contract, ContractAppendix, Document); <c>Details</c> the JSON of the
/// change or business action.
/// </summary>
public sealed record ContractHistoryEntryDto(DateTimeOffset At, string? UserName, string Action, string Subject, string? Note, string? Details);

public sealed record ContractDto(
    Guid Id,
    string Number,
    string Name,
    ProcurementType ProcurementType,
    ContractKind Kind,
    bool RequiresWarehouseReceipt,
    Guid? BidDecisionId,
    string? BidDecisionNumber,
    Guid SupplierId,
    string SupplierName,
    string SupplierTaxCode,
    Guid CategoryId,
    string CategoryName,
    IReadOnlyList<Guid> FacilityIds,
    IReadOnlyList<string> FacilityNames,
    DateOnly? SignedDate,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    string? Duration,
    decimal AmountBeforeVat,
    decimal VatAmount,
    decimal TotalAmount,
    string? PaymentTerms,
    string? WarrantyTerms,
    string? DeliveryTerms,
    string? GuaranteeNumber,
    string? GuaranteeBank,
    decimal? GuaranteeAmount,
    DateOnly? GuaranteeExpiry,
    string? Note,
    Guid? TemplateId,
    string? TemplateName,
    Guid? DocumentId,
    ContractStatus Status,
    string? LastReturnReason,
    DateTimeOffset? EffectiveAt,
    IReadOnlyList<ContractItemDto> Items,
    IReadOnlyList<ContractAppendixDto> Appendices,
    ContractActionsDto Actions);

/// <summary>A goods line: an existing item (bid item or catalog item) or a new item typed in (ngoài thầu).</summary>
public sealed record ContractLineRequest(Guid? ItemId, NewItemRequest? NewItem, decimal Quantity, decimal UnitPrice, decimal VatRate);

public sealed record NewItemRequest(string Code, string Name, string Unit, string? ActiveIngredient, string? Specification, string? Manufacturer, string? Country, string? HisKey);

public sealed record SaveContractRequest(
    ProcurementType ProcurementType,
    Guid? BidDecisionId,
    Guid? SupplierId,
    SaveSupplierRequest? NewSupplier,
    string Number,
    string Name,
    ContractKind Kind,
    bool RequiresWarehouseReceipt,
    Guid CategoryId,
    IReadOnlyList<Guid>? FacilityIds,
    string? Note,
    DateOnly? SignedDate,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    string? Duration,
    string? PaymentTerms,
    string? WarrantyTerms,
    string? DeliveryTerms,
    string? GuaranteeNumber,
    string? GuaranteeBank,
    decimal? GuaranteeAmount,
    DateOnly? GuaranteeExpiry,
    Guid TemplateId,
    IReadOnlyList<ContractLineRequest> Items,
    bool RegenerateDocument = true);

public sealed record SupplierResponseRequest(bool Agree, string? Comment);

public sealed record ResolveRevisionsRequest(Guid VersionId, IReadOnlyList<RevisionDecision> Decisions);

public sealed record RevisionDecision(string Id, bool Accept);

public sealed record RevisionListDto(Guid DocumentId, Guid VersionId, int VersionNumber, IReadOnlyList<DocxRevision> Revisions);

public sealed record SaveAppendixRequest(
    string? Number,
    AppendixType Type,
    string Reason,
    string? InfoChanges,
    DateOnly? NewExpiryDate,
    Guid? TemplateId,
    IReadOnlyList<AppendixLineRequest> Lines,
    IReadOnlyList<AppendixInfoDraft>? InfoFields = null);

public sealed record AppendixLineRequest(Guid ContractItemId, decimal? NewQuantity, decimal? NewUnitPrice);

internal sealed class SaveContractRequestValidator : AbstractValidator<SaveContractRequest>
{
    public SaveContractRequestValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithName("Số hợp đồng").MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().WithName("Tên hợp đồng").MaximumLength(500);
        RuleFor(x => x.CategoryId).NotEmpty().WithName("Hạng mục");
        RuleFor(x => x.TemplateId).NotEmpty().WithName("Mẫu hợp đồng");
        RuleFor(x => x.BidDecisionId).NotEmpty().When(x => x.ProcurementType == ProcurementType.Bidding).WithName("Quyết định thầu");
        RuleFor(x => x).Must(x => x.SupplierId is not null || (x.ProcurementType == ProcurementType.NonBidding && x.NewSupplier is not null))
            .WithName("Nhà cung cấp").WithMessage("Chọn nhà cung cấp (hoặc tạo mới với hợp đồng ngoài thầu).");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Hợp đồng phải có ít nhất một mặt hàng.");
        RuleForEach(x => x.Items).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithName("Số lượng");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithName("Đơn giá");
            l.RuleFor(x => x.VatRate).InclusiveBetween(0, 100).WithName("Thuế suất");
            l.RuleFor(x => x).Must(x => x.ItemId is not null || x.NewItem is not null).WithMessage("Chọn mặt hàng hoặc nhập mặt hàng mới.");
            l.RuleFor(x => x.NewItem!.Code).NotEmpty().When(x => x.NewItem is not null).WithName("Mã hàng");
            l.RuleFor(x => x.NewItem!.Name).NotEmpty().When(x => x.NewItem is not null).WithName("Tên hàng");
            l.RuleFor(x => x.NewItem!.Unit).NotEmpty().When(x => x.NewItem is not null).WithName("Đơn vị tính");
        });
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.EffectiveDate).When(x => x.ExpiryDate is not null && x.EffectiveDate is not null)
            .WithName("Ngày hết hạn").WithMessage("Ngày hết hạn phải sau ngày hiệu lực.");
    }
}

internal sealed class SaveAppendixRequestValidator : AbstractValidator<SaveAppendixRequest>
{
    public SaveAppendixRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithName("Lý do điều chỉnh").MaximumLength(2000);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.NewExpiryDate).NotEmpty().When(x => x.Type == AppendixType.Extension).WithName("Ngày hết hạn mới");
        RuleFor(x => x.InfoChanges).NotEmpty().When(x => x.Type == AppendixType.InfoChange && (x.InfoFields is null || x.InfoFields.Count == 0))
            .WithName("Nội dung thay đổi").WithMessage("Nhập nội dung thay đổi hoặc chọn ít nhất một thông tin hợp đồng cần đổi.");
        RuleFor(x => x.Lines).NotEmpty().When(x => x.Type is AppendixType.AddQuantity or AppendixType.PriceReduction)
            .WithMessage("Chọn ít nhất một mặt hàng điều chỉnh.");
    }
}
