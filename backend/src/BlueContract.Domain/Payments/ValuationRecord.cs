using BlueContract.Domain.Common;
using BlueContract.Domain.Workflows;

namespace BlueContract.Domain.Payments;

public enum PaymentStatus
{
    Unpaid = 0,
    Paid = 1,
}

public sealed record ValuationAcceptanceDraft(Guid AcceptanceId, string Number, string PeriodText, decimal Amount);

/// <summary>Dòng khối lượng của 08a: SL/giá trị theo HĐ, lũy kế đến kỳ trước, kỳ này.</summary>
public sealed record ValuationItemDraft(
    Guid ContractItemId,
    string ItemName,
    string Unit,
    decimal ContractQuantity,
    decimal UnitPrice,
    decimal PreviousQuantity,
    decimal CurrentQuantity);

/// <summary>Số liệu tổng của 08a; lũy kế đến kỳ trước do service tính từ các biên bản trước của hợp đồng.</summary>
public sealed record ValuationFigures(decimal ContractValue, decimal AdvanceAmount, decimal PreviousCumulative, decimal AdvanceRecovered);

/// <summary>
/// Biên bản xác định giá trị khối lượng hoàn thành (Mẫu 08a, spec §5.6, §6.5) + Đề nghị thanh toán kèm theo. Gộp một hoặc
/// nhiều biên bản nghiệm thu đã ký; giá trị kỳ này, lũy kế, % so với HĐ; Ủy nhiệm chi bổ sung được cả sau khi ký.
/// </summary>
public sealed class ValuationRecord : Entity, ISupplierScoped
{
    private readonly List<ValuationAcceptance> _acceptances = [];
    private readonly List<ValuationItem> _items = [];
    private readonly List<PaymentOrder> _paymentOrders = [];

    private ValuationRecord()
    {
        Number = string.Empty;
        PeriodText = string.Empty;
        PaymentRequestNumber = string.Empty;
    }

    public ValuationRecord(string number, string paymentRequestNumber, Guid contractId, Guid supplierId, int paymentSequence, string periodText)
    {
        if (paymentSequence < 1)
        {
            throw new DomainException("VALUATION_SEQUENCE_INVALID");
        }

        Number = Guard.Required(number, "VALUATION_NUMBER_REQUIRED");
        PaymentRequestNumber = Guard.Required(paymentRequestNumber, "VALUATION_NUMBER_REQUIRED");
        ContractId = contractId;
        SupplierId = supplierId;
        PaymentSequence = paymentSequence;
        PeriodText = Guard.Required(periodText, "VALUATION_PERIOD_REQUIRED");
        Status = PaymentRecordStatus.Draft;
        PaymentRequestStatus = PaymentRecordStatus.Draft;
    }

    public string Number { get; private set; }

    public Guid ContractId { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>Lần thanh toán thứ.</summary>
    public int PaymentSequence { get; private set; }

    public string PeriodText { get; private set; }

    public decimal ContractValue { get; private set; }

    public decimal AdvanceAmount { get; private set; }

    public decimal PreviousCumulative { get; private set; }

    public decimal CurrentPeriodValue { get; private set; }

    public decimal CumulativeToDate => PreviousCumulative + CurrentPeriodValue;

    public decimal CompletionPercent => ContractValue == 0 ? 0 : decimal.Round(CumulativeToDate * 100 / ContractValue, 2);

    public decimal AdvanceRecovered { get; private set; }

    /// <summary>Giá trị đề nghị thanh toán kỳ này = giá trị kỳ này − thu hồi tạm ứng.</summary>
    public decimal RequestedAmount => CurrentPeriodValue - AdvanceRecovered;

    public decimal PaidAmount => _paymentOrders.Sum(p => p.Amount);

    public PaymentStatus PaymentStatus { get; private set; }

    /// <summary>Đánh dấu "đã thanh toán" khi chốt công nợ ở biên bản thanh lý (không có đủ UNC).</summary>
    public Guid? SettledByLiquidationId { get; private set; }

    public PaymentRecordStatus Status { get; private set; }

    public Guid? DocumentId { get; private set; }

    public string PaymentRequestNumber { get; private set; }

    public Guid? PaymentRequestDocumentId { get; private set; }

    public PaymentRecordStatus PaymentRequestStatus { get; private set; }

    public string? LastReturnReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Biên bản thanh lý đang dùng (giữ chỗ).</summary>
    public Guid? LiquidationId { get; private set; }

    public IReadOnlyCollection<ValuationAcceptance> Acceptances => _acceptances;

    public IReadOnlyCollection<ValuationItem> Items => _items;

    public IReadOnlyCollection<PaymentOrder> PaymentOrders => _paymentOrders;

    public bool IsEditable => Status is PaymentRecordStatus.Draft or PaymentRecordStatus.Returned;

    public void SetContent(string periodText, ValuationFigures figures, IReadOnlyCollection<ValuationAcceptanceDraft> acceptances, IReadOnlyCollection<ValuationItemDraft> items)
    {
        EnsureEditable();
        if (acceptances.Count == 0)
        {
            throw new DomainException("VALUATION_ACCEPTANCES_REQUIRED");
        }

        if (figures.AdvanceRecovered < 0 || figures.AdvanceAmount < 0 || figures.AdvanceRecovered > figures.AdvanceAmount)
        {
            throw new DomainException("VALUATION_ADVANCE_INVALID");
        }

        PeriodText = Guard.Required(periodText, "VALUATION_PERIOD_REQUIRED");
        _acceptances.Clear();
        _acceptances.AddRange(acceptances.Select(a => new ValuationAcceptance(Id, a)));
        _items.Clear();
        _items.AddRange(items.Select(i => new ValuationItem(Id, i)));
        ContractValue = figures.ContractValue;
        AdvanceAmount = figures.AdvanceAmount;
        PreviousCumulative = figures.PreviousCumulative;
        AdvanceRecovered = figures.AdvanceRecovered;
        CurrentPeriodValue = _acceptances.Sum(a => a.Amount);
        if (CurrentPeriodValue <= 0 || AdvanceRecovered > CurrentPeriodValue)
        {
            throw new DomainException("VALUATION_AMOUNT_INVALID");
        }

        RefreshPaymentStatus();
    }

    public void AttachDocuments(Guid valuationDocumentId, Guid paymentRequestDocumentId)
    {
        DocumentId = valuationDocumentId;
        PaymentRequestDocumentId = paymentRequestDocumentId;
    }

    public void ApplyWorkflow(WorkflowStatus workflowStatus, string? returnReason, DateTimeOffset now)
    {
        if (Status == PaymentRecordStatus.Cancelled)
        {
            return;
        }

        (Status, CompletedAt, LastReturnReason) = workflowStatus switch
        {
            WorkflowStatus.Running => (PaymentRecordStatus.Signing, CompletedAt, LastReturnReason),
            WorkflowStatus.Returned => (PaymentRecordStatus.Returned, CompletedAt, returnReason),
            WorkflowStatus.Completed => (PaymentRecordStatus.Completed, CompletedAt ?? now, LastReturnReason),
            WorkflowStatus.Cancelled => (PaymentRecordStatus.Draft, CompletedAt, LastReturnReason),
            _ => (Status, CompletedAt, LastReturnReason),
        };
    }

    /// <summary>Đề nghị thanh toán đi luồng ký riêng (NCC → Giám đốc).</summary>
    public void ApplyPaymentRequestWorkflow(WorkflowStatus workflowStatus)
    {
        if (Status == PaymentRecordStatus.Cancelled)
        {
            return;
        }

        PaymentRequestStatus = workflowStatus switch
        {
            WorkflowStatus.Running => PaymentRecordStatus.Signing,
            WorkflowStatus.Returned => PaymentRecordStatus.Returned,
            WorkflowStatus.Completed => PaymentRecordStatus.Completed,
            WorkflowStatus.Cancelled => PaymentRecordStatus.Draft,
            _ => PaymentRequestStatus,
        };
    }

    /// <summary>Ủy nhiệm chi (đính kèm được cả sau khi ký, spec §6.5).</summary>
    public PaymentOrder AddPaymentOrder(string number, DateOnly date, decimal amount, string? fileKey, string? fileName, string? addedByName, DateTimeOffset now)
    {
        if (Status == PaymentRecordStatus.Cancelled || SettledByLiquidationId is not null)
        {
            throw new DomainException("VALUATION_LOCKED");
        }

        if (amount <= 0)
        {
            throw new DomainException("PAYMENT_ORDER_AMOUNT_INVALID");
        }

        if (PaidAmount + amount > RequestedAmount)
        {
            throw new DomainException("PAYMENT_ORDER_EXCEEDS_REQUESTED");
        }

        var order = new PaymentOrder(Id, Guard.Required(number, "PAYMENT_ORDER_NUMBER_REQUIRED"), date, amount, fileKey, fileName, addedByName, now);
        _paymentOrders.Add(order);
        RefreshPaymentStatus();
        return order;
    }

    public void RemovePaymentOrder(Guid orderId)
    {
        if (LiquidationId is not null || SettledByLiquidationId is not null)
        {
            throw new DomainException("VALUATION_LOCKED");
        }

        var order = _paymentOrders.FirstOrDefault(p => p.Id == orderId) ?? throw new DomainException("PAYMENT_ORDER_NOT_FOUND");
        _paymentOrders.Remove(order);
        RefreshPaymentStatus();
    }

    public void Cancel()
    {
        if (Status == PaymentRecordStatus.Completed || LiquidationId is not null)
        {
            throw new DomainException("VALUATION_NOT_CANCELLABLE");
        }

        Status = PaymentRecordStatus.Cancelled;
        PaymentRequestStatus = PaymentRecordStatus.Cancelled;
    }

    public void ReserveForLiquidation(Guid liquidationId)
    {
        if (Status != PaymentRecordStatus.Completed)
        {
            throw new DomainException("VALUATION_NOT_COMPLETED");
        }

        LiquidationId = liquidationId;
    }

    public void ReleaseFromLiquidation(Guid liquidationId)
    {
        if (LiquidationId == liquidationId)
        {
            LiquidationId = null;
        }
    }

    /// <summary>Chốt công nợ: khoản còn thiếu được đánh dấu "đã thanh toán" bằng chứng từ trong biên bản thanh lý.</summary>
    public void MarkSettled(Guid liquidationId)
    {
        SettledByLiquidationId = liquidationId;
        PaymentStatus = PaymentStatus.Paid;
    }

    private void RefreshPaymentStatus() =>
        PaymentStatus = SettledByLiquidationId is not null || (RequestedAmount > 0 && PaidAmount >= RequestedAmount) ? PaymentStatus.Paid : PaymentStatus.Unpaid;

    private void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new DomainException("VALUATION_LOCKED");
        }
    }
}

public sealed class ValuationAcceptance
{
    private ValuationAcceptance()
    {
        Number = string.Empty;
        PeriodText = string.Empty;
    }

    internal ValuationAcceptance(Guid valuationId, ValuationAcceptanceDraft d)
    {
        Id = Guid.CreateVersion7();
        ValuationId = valuationId;
        AcceptanceId = d.AcceptanceId;
        Number = d.Number;
        PeriodText = d.PeriodText;
        Amount = d.Amount;
    }

    public Guid Id { get; private set; }

    public Guid ValuationId { get; private set; }

    public Guid AcceptanceId { get; private set; }

    public string Number { get; private set; }

    public string PeriodText { get; private set; }

    public decimal Amount { get; private set; }
}

public sealed class ValuationItem
{
    private ValuationItem()
    {
        ItemName = string.Empty;
        Unit = string.Empty;
    }

    internal ValuationItem(Guid valuationId, ValuationItemDraft d)
    {
        Id = Guid.CreateVersion7();
        ValuationId = valuationId;
        ContractItemId = d.ContractItemId;
        ItemName = d.ItemName;
        Unit = d.Unit;
        ContractQuantity = d.ContractQuantity;
        UnitPrice = d.UnitPrice;
        PreviousQuantity = d.PreviousQuantity;
        CurrentQuantity = d.CurrentQuantity;
    }

    public Guid Id { get; private set; }

    public Guid ValuationId { get; private set; }

    public Guid ContractItemId { get; private set; }

    public string ItemName { get; private set; }

    public string Unit { get; private set; }

    public decimal ContractQuantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal PreviousQuantity { get; private set; }

    public decimal CurrentQuantity { get; private set; }

    public decimal CumulativeQuantity => PreviousQuantity + CurrentQuantity;

    public decimal ContractAmount => Money(ContractQuantity);

    public decimal PreviousAmount => Money(PreviousQuantity);

    public decimal CurrentAmount => Money(CurrentQuantity);

    public decimal CumulativeAmount => Money(CumulativeQuantity);

    private decimal Money(decimal quantity) => decimal.Round(quantity * UnitPrice, 0, MidpointRounding.AwayFromZero);
}

/// <summary>Ủy nhiệm chi (chứng từ chuyển tiền của Kho bạc/ngân hàng).</summary>
public sealed class PaymentOrder
{
    private PaymentOrder()
    {
        Number = string.Empty;
    }

    internal PaymentOrder(Guid valuationId, string number, DateOnly date, decimal amount, string? fileKey, string? fileName, string? addedByName, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        ValuationId = valuationId;
        Number = number;
        Date = date;
        Amount = amount;
        FileKey = fileKey;
        FileName = fileName;
        AddedByName = addedByName;
        AddedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid ValuationId { get; private set; }

    public string Number { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public string? FileKey { get; private set; }

    public string? FileName { get; private set; }

    public string? AddedByName { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}
