using BlueContract.Domain.Receipts;
using FluentValidation;

namespace BlueContract.Application.Receipts;

public sealed record ReceiptListItemDto(
    Guid Id,
    string Number,
    ReceiptType Type,
    ReceiptSource Source,
    DateOnly ReceiptDate,
    string WarehouseName,
    Guid? SupplierId,
    string SupplierName,
    Guid? ContractId,
    string? ContractNumber,
    string? InvoiceNumber,
    decimal TotalAmount,
    ReceiptStatus Status,
    ReceiptValidationStatus ValidationStatus,
    int IssueCount,
    bool HasInvoice,
    bool IsIncludedInAcceptance,
    DateTimeOffset CreatedAt);

public sealed record ReceiptFilter(
    ReceiptStatus? Status,
    ReceiptValidationStatus? Validation,
    ReceiptType? Type,
    ReceiptSource? Source,
    Guid? SupplierId,
    Guid? WarehouseId,
    Guid? ContractId,
    DateOnly? From,
    DateOnly? To,
    bool? HasInvoice,
    bool? Accepted);

public sealed record ReceiptLineDto(
    Guid Id,
    int LineNo,
    string? HisKey,
    string ItemCode,
    string ItemName,
    string Unit,
    string? LotNumber,
    DateOnly? ExpiryDate,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal Amount,
    decimal VatAmount,
    Guid? ContractItemId,
    decimal? ContractUnitPrice,
    decimal? ContractRemaining);

public sealed record ReceiptInvoiceDto(
    string? Symbol,
    string? Number,
    DateOnly? Date,
    decimal? AmountBeforeVat,
    decimal? VatAmount,
    decimal? TotalAmount,
    string? SellerTaxCode,
    bool HasXml,
    bool HasPdf,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByName);

public sealed record ReceiptIssueDto(string Code, ReceiptValidationStatus Severity, string Message, int? LineNo);

public sealed record ReceiptDto(
    Guid Id,
    string Number,
    ReceiptType Type,
    ReceiptSource Source,
    string? HisReceiptId,
    DateOnly ReceiptDate,
    string WarehouseCode,
    Guid? WarehouseId,
    string WarehouseName,
    Guid? SupplierId,
    string SupplierName,
    string SupplierTaxCode,
    string? BidDecisionNumber,
    Guid? ContractId,
    string? ContractNumber,
    Guid? CallOrderId,
    string? CallOrderNumber,
    string? DelivererName,
    string? Note,
    ReceiptInvoiceDto Invoice,
    ReceiptStatus Status,
    ReceiptValidationStatus ValidationStatus,
    DateTimeOffset? ValidatedAt,
    IReadOnlyList<ReceiptIssueDto> Issues,
    Guid? DocumentId,
    DateTimeOffset? CompletedAt,
    string? LastReturnReason,
    bool IsIncludedInAcceptance,
    decimal TotalAmount,
    decimal VatAmount,
    IReadOnlyList<ReceiptLineDto> Lines,
    bool CanEdit,
    bool CanEditInvoice,
    bool CanSubmit,
    bool CanCancel,
    bool CanRevalidate);

public sealed record SaveReceiptLineRequest(Guid ContractItemId, decimal Quantity, string? LotNumber, DateOnly? ExpiryDate, decimal? UnitPrice);

/// <summary>Phiếu lập trên cổng (Portal) hoặc phiếu ảo xác nhận khối lượng (Virtual = true).</summary>
public sealed record SaveReceiptRequest(
    ReceiptType Type,
    bool Virtual,
    Guid ContractId,
    Guid? WarehouseId,
    DateOnly ReceiptDate,
    Guid? CallOrderId,
    string? DelivererName,
    string? Note,
    IReadOnlyList<SaveReceiptLineRequest> Lines);

public sealed record InvoiceRequest(string Symbol, string Number, DateOnly Date, decimal? AmountBeforeVat, decimal? VatAmount, decimal TotalAmount, string? SellerTaxCode);

/// <summary>Kết quả đọc file XML hóa đơn điện tử (điền sẵn form).</summary>
public sealed record ParsedInvoiceDto(InvoiceRequest Invoice, string? SellerName, string? BuyerTaxCode, string? BuyerName);

internal sealed class SaveReceiptRequestValidator : AbstractValidator<SaveReceiptRequest>
{
    public SaveReceiptRequestValidator()
    {
        RuleFor(x => x.ContractId).NotEmpty().WithName("Hợp đồng");
        RuleFor(x => x.WarehouseId).NotEmpty().When(x => !x.Virtual).WithName("Kho");
        RuleFor(x => x.ReceiptDate).NotEmpty().WithName("Ngày phiếu");
        RuleFor(x => x.DelivererName).MaximumLength(200);
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Nhập số lượng cho ít nhất một mặt hàng.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithName("Số lượng");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).When(x => x.UnitPrice is not null).WithName("Đơn giá");
            l.RuleFor(x => x.LotNumber).MaximumLength(60);
        });
    }
}

internal sealed class InvoiceRequestValidator : AbstractValidator<InvoiceRequest>
{
    public InvoiceRequestValidator()
    {
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(20).WithName("Ký hiệu hóa đơn");
        RuleFor(x => x.Number).NotEmpty().MaximumLength(20).WithName("Số hóa đơn");
        RuleFor(x => x.Date).NotEmpty().WithName("Ngày hóa đơn");
        RuleFor(x => x.TotalAmount).GreaterThan(0).WithName("Tổng tiền thanh toán");
        RuleFor(x => x.SellerTaxCode).MaximumLength(20);
    }
}
