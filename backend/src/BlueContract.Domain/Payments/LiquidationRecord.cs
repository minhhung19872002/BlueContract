using BlueContract.Domain.Common;
using BlueContract.Domain.Workflows;

namespace BlueContract.Domain.Payments;

public sealed record LiquidationValuationDraft(Guid ValuationId, string Number, DateOnly Date, decimal Amount, decimal PaidAmount, string? PaymentOrderNumbers);

/// <summary>
/// Biên bản thanh lý hợp đồng (spec §5.6, §6.5): đối chiếu Giá trị HĐ / thực hiện / đã thanh toán / còn lại; các khoản còn
/// thiếu được đánh dấu "đã thanh toán" bằng chứng từ để chốt công nợ. Khi ký xong, hợp đồng chuyển Đã thanh lý.
/// </summary>
public sealed class LiquidationRecord : Entity, ISupplierScoped
{
    private readonly List<LiquidationValuation> _valuations = [];
    private readonly List<LiquidationSettlement> _settlements = [];

    private LiquidationRecord()
    {
        Number = string.Empty;
    }

    public LiquidationRecord(string number, Guid contractId, Guid supplierId)
    {
        Number = Guard.Required(number, "LIQUIDATION_NUMBER_REQUIRED");
        ContractId = contractId;
        SupplierId = supplierId;
        Status = PaymentRecordStatus.Draft;
    }

    public string Number { get; private set; }

    public Guid ContractId { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>Giá trị hợp đồng (kể cả phụ lục).</summary>
    public decimal ContractValue { get; private set; }

    /// <summary>Giá trị thực hiện = lũy kế các 08a đã ký.</summary>
    public decimal ExecutedValue { get; private set; }

    /// <summary>Đã thanh toán theo Ủy nhiệm chi.</summary>
    public decimal PaidAmount { get; private set; }

    /// <summary>Khoản được đánh dấu đã thanh toán khi chốt công nợ.</summary>
    public decimal SettledAmount => _settlements.Sum(s => s.Amount);

    /// <summary>Còn phải thanh toán (âm = thanh toán thừa).</summary>
    public decimal RemainingAmount => ExecutedValue - PaidAmount - SettledAmount;

    /// <summary>Giá trị giảm trừ / không thực hiện.</summary>
    public decimal ReductionAmount => Math.Max(0, ContractValue - ExecutedValue);

    public string? Notes { get; private set; }

    public PaymentRecordStatus Status { get; private set; }

    public Guid? DocumentId { get; private set; }

    public string? LastReturnReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyCollection<LiquidationValuation> Valuations => _valuations;

    public IReadOnlyCollection<LiquidationSettlement> Settlements => _settlements;

    public bool IsEditable => Status is PaymentRecordStatus.Draft or PaymentRecordStatus.Returned;

    public void SetFigures(decimal contractValue, string? notes, IReadOnlyCollection<LiquidationValuationDraft> valuations)
    {
        EnsureEditable();
        if (valuations.Count == 0)
        {
            throw new DomainException("LIQUIDATION_VALUATIONS_REQUIRED");
        }

        _valuations.Clear();
        _valuations.AddRange(valuations.Select(v => new LiquidationValuation(Id, v)));
        ContractValue = contractValue;
        ExecutedValue = _valuations.Sum(v => v.Amount);
        PaidAmount = _valuations.Sum(v => v.PaidAmount);
        Notes = Guard.Optional(notes);

        // Settlements of valuations no longer listed are dropped.
        _settlements.RemoveAll(s => s.ValuationId is { } id && _valuations.All(v => v.ValuationId != id));
    }

    /// <summary>Đánh dấu một khoản chưa thanh toán thành "đã thanh toán" (nhập chứng từ) — lưu ai, khi nào.</summary>
    public LiquidationSettlement AddSettlement(Guid? valuationId, string description, decimal amount, string? voucherNumber, DateOnly? voucherDate,
        string? fileKey, string? fileName, Guid userId, string? userName, DateTimeOffset now)
    {
        EnsureEditable();
        if (amount <= 0)
        {
            throw new DomainException("SETTLEMENT_AMOUNT_INVALID");
        }

        if (amount > RemainingAmount)
        {
            throw new DomainException("SETTLEMENT_EXCEEDS_REMAINING");
        }

        if (valuationId is { } id)
        {
            var valuation = _valuations.FirstOrDefault(v => v.ValuationId == id) ?? throw new DomainException("SETTLEMENT_VALUATION_NOT_FOUND");
            var open = valuation.Amount - valuation.PaidAmount - _settlements.Where(s => s.ValuationId == id).Sum(s => s.Amount);
            if (amount > open)
            {
                throw new DomainException("SETTLEMENT_EXCEEDS_REMAINING");
            }
        }

        var settlement = new LiquidationSettlement(Id, valuationId, Guard.Required(description, "SETTLEMENT_DESCRIPTION_REQUIRED"), amount,
            Guard.Optional(voucherNumber), voucherDate, fileKey, fileName, userId, userName, now);
        _settlements.Add(settlement);
        return settlement;
    }

    public void RemoveSettlement(Guid settlementId)
    {
        EnsureEditable();
        var settlement = _settlements.FirstOrDefault(s => s.Id == settlementId) ?? throw new DomainException("SETTLEMENT_NOT_FOUND");
        _settlements.Remove(settlement);
    }

    public void AttachDocument(Guid documentId) => DocumentId = documentId;

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

    public void Cancel()
    {
        if (Status == PaymentRecordStatus.Completed)
        {
            throw new DomainException("LIQUIDATION_NOT_CANCELLABLE");
        }

        Status = PaymentRecordStatus.Cancelled;
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new DomainException("LIQUIDATION_LOCKED");
        }
    }
}

public sealed class LiquidationValuation
{
    private LiquidationValuation()
    {
        Number = string.Empty;
    }

    internal LiquidationValuation(Guid liquidationId, LiquidationValuationDraft d)
    {
        Id = Guid.CreateVersion7();
        LiquidationId = liquidationId;
        ValuationId = d.ValuationId;
        Number = d.Number;
        Date = d.Date;
        Amount = d.Amount;
        PaidAmount = d.PaidAmount;
        PaymentOrderNumbers = d.PaymentOrderNumbers;
    }

    public Guid Id { get; private set; }

    public Guid LiquidationId { get; private set; }

    public Guid ValuationId { get; private set; }

    public string Number { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Giá trị đề nghị thanh toán của lần đó.</summary>
    public decimal Amount { get; private set; }

    public decimal PaidAmount { get; private set; }

    public string? PaymentOrderNumbers { get; private set; }
}

public sealed class LiquidationSettlement
{
    private LiquidationSettlement()
    {
        Description = string.Empty;
    }

    internal LiquidationSettlement(Guid liquidationId, Guid? valuationId, string description, decimal amount, string? voucherNumber, DateOnly? voucherDate,
        string? fileKey, string? fileName, Guid userId, string? userName, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        LiquidationId = liquidationId;
        ValuationId = valuationId;
        Description = description;
        Amount = amount;
        VoucherNumber = voucherNumber;
        VoucherDate = voucherDate;
        FileKey = fileKey;
        FileName = fileName;
        MarkedBy = userId;
        MarkedByName = userName;
        MarkedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid LiquidationId { get; private set; }

    public Guid? ValuationId { get; private set; }

    public string Description { get; private set; }

    public decimal Amount { get; private set; }

    public string? VoucherNumber { get; private set; }

    public DateOnly? VoucherDate { get; private set; }

    public string? FileKey { get; private set; }

    public string? FileName { get; private set; }

    public Guid MarkedBy { get; private set; }

    public string? MarkedByName { get; private set; }

    public DateTimeOffset MarkedAt { get; private set; }
}
