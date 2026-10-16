using BlueContract.Domain.Receipts;
using FluentValidation;

namespace BlueContract.Application.Integration;

/// <summary>Hóa đơn kèm phiếu (optional: the supplier may add it later on the portal).</summary>
public sealed record HisInvoicePayload(
    string Symbol,
    string Number,
    DateOnly Date,
    decimal? AmountBeforeVat,
    decimal? VatAmount,
    decimal TotalAmount,
    string? SellerTaxCode);

public sealed record HisReceiptLinePayload(
    string? HisKey,
    string ItemCode,
    string ItemName,
    string? Unit,
    string? LotNumber,
    DateOnly? ExpiryDate,
    decimal Quantity,
    decimal UnitPrice,
    decimal? VatRate);

/// <summary>
/// Phiếu nhập kho / hoàn trả as sent by the HIS (spec §6.4 B1, docs/HIS_INTEGRATION.md). Linked to a contract by
/// (BidDecisionNumber + SupplierTaxCode); idempotent by <see cref="HisReceiptId"/>.
/// </summary>
public sealed record HisReceiptPayload(
    string HisReceiptId,
    string? ReceiptNumber,
    ReceiptType Type,
    DateOnly ReceiptDate,
    string WarehouseCode,
    string? BidDecisionNumber,
    string SupplierTaxCode,
    string? SupplierName,
    string? ContractNumber,
    string? CallOrderNumber,
    string? DelivererName,
    string? Note,
    HisInvoicePayload? Invoice,
    IReadOnlyList<HisReceiptLinePayload> Lines);

public enum HisIngestOutcome
{
    Created = 0,
    Updated = 1,

    /// <summary>Same HisReceiptId and identical content: nothing changed.</summary>
    Duplicate = 2,
}

public sealed record HisIngestIssueDto(string Code, ReceiptValidationStatus Severity, string Message, int? LineNo);

public sealed record HisIngestResultDto(
    Guid ReceiptId,
    string ReceiptNumber,
    HisIngestOutcome Outcome,
    ReceiptStatus Status,
    ReceiptValidationStatus ValidationStatus,
    string? ContractNumber,
    IReadOnlyList<HisIngestIssueDto> Issues);

internal sealed class HisReceiptPayloadValidator : AbstractValidator<HisReceiptPayload>
{
    public HisReceiptPayloadValidator()
    {
        RuleFor(x => x.HisReceiptId).NotEmpty().MaximumLength(100).WithName("Mã phiếu HIS (hisReceiptId)");
        RuleFor(x => x.ReceiptNumber).MaximumLength(60);
        RuleFor(x => x.ReceiptDate).NotEmpty().WithName("Ngày phiếu (receiptDate)");
        RuleFor(x => x.WarehouseCode).NotEmpty().MaximumLength(60).WithName("Mã kho (warehouseCode)");
        RuleFor(x => x.SupplierTaxCode).NotEmpty().MaximumLength(20).WithName("MST nhà cung cấp (supplierTaxCode)");
        RuleFor(x => x.BidDecisionNumber).MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty().WithName("Chi tiết phiếu (lines)");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemCode).NotEmpty().MaximumLength(60).WithName("Mã hàng (itemCode)");
            l.RuleFor(x => x.ItemName).NotEmpty().MaximumLength(500).WithName("Tên hàng (itemName)");
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithName("Số lượng (quantity)");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithName("Đơn giá (unitPrice)");
            l.RuleFor(x => x.VatRate).InclusiveBetween(0, 100).When(x => x.VatRate is not null).WithName("Thuế suất (vatRate)");
        });
        When(x => x.Invoice is not null, () =>
        {
            RuleFor(x => x.Invoice!.Symbol).NotEmpty().MaximumLength(20).WithName("Ký hiệu hóa đơn");
            RuleFor(x => x.Invoice!.Number).NotEmpty().MaximumLength(20).WithName("Số hóa đơn");
            RuleFor(x => x.Invoice!.TotalAmount).GreaterThan(0).WithName("Tổng tiền hóa đơn");
        });
    }
}
