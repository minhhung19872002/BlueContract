using System.Globalization;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Receipts;

namespace BlueContract.Application.Receipts.Validation;

/// <summary>Everything the rules need, loaded once by <see cref="ReceiptValidationLoader"/> (rules themselves are pure).</summary>
public sealed class ReceiptValidationContext
{
    public required WarehouseReceipt Receipt { get; init; }

    /// <summary>NCC tìm được theo MST.</summary>
    public bool SupplierFound { get; init; }

    /// <summary>Hợp đồng map được theo (Số QĐ + MST) — may be expired / liquidated (rules report it).</summary>
    public Contract? Contract { get; init; }

    /// <summary>Net received quantity (receipts − returns) by contract item from other valid receipts of the contract.</summary>
    public IReadOnlyDictionary<Guid, decimal> ReceivedByOthers { get; init; } = new Dictionary<Guid, decimal>();

    /// <summary>Linked call order: confirmed quantity minus what other receipts already delivered, by contract item.</summary>
    public IReadOnlyDictionary<Guid, decimal>? CallOrderOutstanding { get; init; }

    public string? CallOrderNumber { get; init; }

    /// <summary>Số phiếu khác (chưa hủy) dùng cùng ký hiệu + số hóa đơn của cùng NCC.</summary>
    public IReadOnlyList<string> DuplicateInvoiceReceipts { get; init; } = [];

    public decimal InvoiceRoundingTolerance { get; init; } = 1000;

    public int ExpiryWarningDays { get; init; } = 180;
}

/// <summary>One check of the validation engine (spec §5.5: "mỗi rule là một class").</summary>
public interface IReceiptRule
{
    IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c);
}

/// <summary>Runs every rule; Invalid blocks signing, Warning requires the signer's reason (see ReceiptWorkflowGuard).</summary>
public sealed class ReceiptValidator(IEnumerable<IReceiptRule> rules)
{
    public static IReadOnlyList<IReceiptRule> DefaultRules { get; } =
    [
        new SupplierAndContractRule(),
        new ContractStateRule(),
        new ItemInContractRule(),
        new UnitPriceRule(),
        new ContractRemainingRule(),
        new CalledQuantityRule(),
        new InvoiceRequiredRule(),
        new InvoiceTotalRule(),
        new ExpiryDateRule(),
        new DuplicateInvoiceRule(),
    ];

    public IReadOnlyList<ReceiptIssue> Validate(ReceiptValidationContext context) => [.. rules.SelectMany(r => r.Evaluate(context))];
}

internal static class Fmt
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static string Qty(decimal v) => v.ToString("#,##0.###", Vi);

    public static string Money(decimal v) => v.ToString("#,##0", Vi) + " ₫";

    public static string Date(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

/// <summary>Không tìm thấy NCC theo MST / hợp đồng hiệu lực theo (Số QĐ + MST).</summary>
public sealed class SupplierAndContractRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        var r = c.Receipt;
        if (!c.SupplierFound)
        {
            yield return new("SUPPLIER_NOT_FOUND", ReceiptValidationStatus.Invalid, $"Không tìm thấy nhà cung cấp có MST {r.SupplierTaxCode} trên cổng.");
        }
        else if (c.Contract is null)
        {
            yield return new("CONTRACT_NOT_FOUND", ReceiptValidationStatus.Invalid,
                $"Không tìm thấy hợp đồng hiệu lực theo Số QĐ {r.BidDecisionNumber ?? "(trống)"} và MST {r.SupplierTaxCode}.");
        }
    }
}

/// <summary>Hợp đồng hết hạn (cảnh báo) / đã thanh lý, hủy hoặc chưa hiệu lực (không hợp lệ).</summary>
public sealed class ContractStateRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        if (c.Contract is not { } contract)
        {
            yield break;
        }

        if (contract.Status is ContractStatus.Liquidated or ContractStatus.Cancelled)
        {
            yield return new("CONTRACT_CLOSED", ReceiptValidationStatus.Invalid, $"Hợp đồng {contract.Number} đã thanh lý/hủy — không nhận thêm phiếu.");
        }
        else if (contract.Status != ContractStatus.Effective)
        {
            yield return new("CONTRACT_NOT_EFFECTIVE", ReceiptValidationStatus.Invalid, $"Hợp đồng {contract.Number} chưa có hiệu lực.");
        }
        else if (contract.ExpiryDate is { } expiry && expiry < c.Receipt.ReceiptDate)
        {
            yield return new("CONTRACT_EXPIRED", ReceiptValidationStatus.Warning,
                $"Hợp đồng {contract.Number} đã hết hạn ngày {Fmt.Date(expiry)} trước ngày phiếu {Fmt.Date(c.Receipt.ReceiptDate)}.");
        }
    }
}

/// <summary>Mặt hàng không thuộc hợp đồng.</summary>
public sealed class ItemInContractRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        if (c.Contract is null)
        {
            yield break;
        }

        foreach (var line in c.Receipt.Lines.Where(l => l.ContractItemId is null))
        {
            yield return new("ITEM_NOT_IN_CONTRACT", ReceiptValidationStatus.Invalid,
                $"Dòng {line.LineNo}: mặt hàng {line.ItemCode} – {line.ItemName} không thuộc hợp đồng {c.Contract.Number}.", line.LineNo);
        }
    }
}

/// <summary>Đơn giá khác đơn giá hợp đồng: cao hơn → không hợp lệ; thấp hơn → cảnh báo.</summary>
public sealed class UnitPriceRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        if (c.Contract is null)
        {
            yield break;
        }

        foreach (var line in c.Receipt.Lines.Where(l => l.ContractItemId is not null))
        {
            var item = c.Contract.Items.First(i => i.Id == line.ContractItemId);
            if (line.UnitPrice > item.UnitPrice)
            {
                yield return new("UNIT_PRICE_ABOVE_CONTRACT", ReceiptValidationStatus.Invalid,
                    $"Dòng {line.LineNo}: đơn giá {Fmt.Money(line.UnitPrice)} cao hơn đơn giá hợp đồng {Fmt.Money(item.UnitPrice)}.", line.LineNo);
            }
            else if (line.UnitPrice < item.UnitPrice)
            {
                yield return new("UNIT_PRICE_MISMATCH", ReceiptValidationStatus.Warning,
                    $"Dòng {line.LineNo}: đơn giá {Fmt.Money(line.UnitPrice)} khác đơn giá hợp đồng {Fmt.Money(item.UnitPrice)}.", line.LineNo);
            }
        }
    }
}

/// <summary>SL vượt SL còn lại của HĐ (nhập) / hoàn trả vượt SL đã nhập.</summary>
public sealed class ContractRemainingRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        if (c.Contract is null)
        {
            yield break;
        }

        foreach (var group in c.Receipt.Lines.Where(l => l.ContractItemId is not null).GroupBy(l => l.ContractItemId!.Value))
        {
            var item = c.Contract.Items.First(i => i.Id == group.Key);
            var quantity = group.Sum(l => l.Quantity);
            var received = c.ReceivedByOthers.GetValueOrDefault(group.Key);
            var lineNo = group.First().LineNo;
            if (c.Receipt.Type == ReceiptType.Return)
            {
                if (quantity > received)
                {
                    yield return new("RETURN_EXCEEDS_RECEIVED", ReceiptValidationStatus.Invalid,
                        $"Dòng {lineNo}: hoàn trả {Fmt.Qty(quantity)} {item.Unit} {item.ItemName} vượt số đã nhập {Fmt.Qty(received)}.", lineNo);
                }
            }
            else if (received + quantity > item.Quantity)
            {
                yield return new("QTY_EXCEEDS_CONTRACT", ReceiptValidationStatus.Invalid,
                    $"Dòng {lineNo}: nhập {Fmt.Qty(quantity)} {item.Unit} {item.ItemName} vượt số lượng còn lại của hợp đồng {Fmt.Qty(Math.Max(0, item.Quantity - received))}.", lineNo);
            }
        }
    }
}

/// <summary>Vượt SL đã gọi (đơn gọi hàng liên kết, hoặc tổng SL đã gọi của HĐ gọi hàng) — cảnh báo.</summary>
public sealed class CalledQuantityRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        if (c.Contract is null || c.Receipt.Type == ReceiptType.Return || c.Contract.Kind != ContractKind.UnitPriceCallOff)
        {
            yield break;
        }

        foreach (var group in c.Receipt.Lines.Where(l => l.ContractItemId is not null).GroupBy(l => l.ContractItemId!.Value))
        {
            var item = c.Contract.Items.First(i => i.Id == group.Key);
            var quantity = group.Sum(l => l.Quantity);
            var lineNo = group.First().LineNo;
            if (c.CallOrderOutstanding is { } outstanding)
            {
                var open = outstanding.GetValueOrDefault(group.Key);
                if (quantity > open)
                {
                    yield return new("QTY_EXCEEDS_CALLED", ReceiptValidationStatus.Warning,
                        $"Dòng {lineNo}: nhập {Fmt.Qty(quantity)} {item.Unit} {item.ItemName} vượt số còn phải giao của đơn gọi hàng {c.CallOrderNumber} ({Fmt.Qty(open)}).", lineNo);
                }
            }
            else
            {
                var received = c.ReceivedByOthers.GetValueOrDefault(group.Key);
                if (received + quantity > item.CalledQuantity)
                {
                    yield return new("QTY_EXCEEDS_CALLED", ReceiptValidationStatus.Warning,
                        $"Dòng {lineNo}: tổng nhập {Fmt.Qty(received + quantity)} {item.Unit} {item.ItemName} vượt số lượng đã gọi hàng {Fmt.Qty(item.CalledQuantity)}.", lineNo);
                }
            }
        }
    }
}

/// <summary>Thiếu hóa đơn — người giao hàng (NCC) phải bổ sung trước khi ký.</summary>
public sealed class InvoiceRequiredRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        if (!c.Receipt.HasInvoice)
        {
            yield return new("INVOICE_MISSING", ReceiptValidationStatus.Warning,
                "Chưa có thông tin hóa đơn — nhà cung cấp bổ sung (tải XML hóa đơn điện tử) trước khi người giao hàng ký.");
        }
    }
}

/// <summary>Tổng tiền hóa đơn ≠ tổng tiền phiếu (vượt sai số làm tròn cấu hình); MST người bán khác MST NCC.</summary>
public sealed class InvoiceTotalRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        var r = c.Receipt;
        if (!r.HasInvoice)
        {
            yield break;
        }

        var difference = Math.Abs(r.InvoiceTotalAmount!.Value - r.TotalAmount);
        if (difference > c.InvoiceRoundingTolerance)
        {
            yield return new("INVOICE_TOTAL_MISMATCH", ReceiptValidationStatus.Warning,
                $"Tổng tiền hóa đơn {Fmt.Money(r.InvoiceTotalAmount.Value)} lệch tổng tiền phiếu {Fmt.Money(r.TotalAmount)} ({Fmt.Money(difference)}, cho phép {Fmt.Money(c.InvoiceRoundingTolerance)}).");
        }

        if (r.InvoiceSellerTaxCode is { } seller && !string.Equals(seller, r.SupplierTaxCode, StringComparison.OrdinalIgnoreCase))
        {
            yield return new("INVOICE_SELLER_MISMATCH", ReceiptValidationStatus.Warning,
                $"MST người bán trên hóa đơn ({seller}) khác MST nhà cung cấp ({r.SupplierTaxCode}).");
        }
    }
}

/// <summary>Hạn dùng: đã hết hạn → không hợp lệ; còn ít hơn ngưỡng cấu hình → cảnh báo.</summary>
public sealed class ExpiryDateRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        var r = c.Receipt;
        if (r.Type == ReceiptType.Return)
        {
            yield break;
        }

        var threshold = r.ReceiptDate.AddDays(c.ExpiryWarningDays);
        foreach (var line in r.Lines.Where(l => l.ExpiryDate is not null))
        {
            if (line.ExpiryDate <= r.ReceiptDate)
            {
                yield return new("ITEM_EXPIRED", ReceiptValidationStatus.Invalid,
                    $"Dòng {line.LineNo}: lô {line.LotNumber ?? "—"} của {line.ItemName} đã hết hạn dùng ({Fmt.Date(line.ExpiryDate.Value)}).", line.LineNo);
            }
            else if (line.ExpiryDate < threshold)
            {
                yield return new("EXPIRY_TOO_SOON", ReceiptValidationStatus.Warning,
                    $"Dòng {line.LineNo}: lô {line.LotNumber ?? "—"} của {line.ItemName} hết hạn {Fmt.Date(line.ExpiryDate.Value)}, còn dưới {c.ExpiryWarningDays} ngày.", line.LineNo);
            }
        }
    }
}

/// <summary>Trùng số hóa đơn với phiếu khác của cùng NCC.</summary>
public sealed class DuplicateInvoiceRule : IReceiptRule
{
    public IEnumerable<ReceiptIssue> Evaluate(ReceiptValidationContext c)
    {
        if (c.Receipt.HasInvoice && c.DuplicateInvoiceReceipts.Count > 0)
        {
            yield return new("INVOICE_DUPLICATE", ReceiptValidationStatus.Invalid,
                $"Hóa đơn {c.Receipt.InvoiceSymbol} số {c.Receipt.InvoiceNumber} đã dùng cho phiếu {string.Join(", ", c.DuplicateInvoiceReceipts)}.");
        }
    }
}
