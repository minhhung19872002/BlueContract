using BlueContract.Domain.Common;
using BlueContract.Domain.Workflows;

namespace BlueContract.Domain.Contracts;

public enum ProcurementType
{
    /// <summary>Trong thầu (từ quyết định thầu).</summary>
    Bidding = 0,

    /// <summary>Ngoài thầu.</summary>
    NonBidding = 1,
}

public enum ContractKind
{
    /// <summary>Theo đơn giá, gọi hàng từng đợt (đề xuất → tổng hợp → gọi hàng).</summary>
    UnitPriceCallOff = 0,

    /// <summary>Trọn gói.</summary>
    LumpSum = 1,
}

/// <summary>Contract lifecycle (spec §5.3). Signing states mirror the active step of the contract workflow.</summary>
public enum ContractStatus
{
    Draft = 0,
    InReview = 1,
    SupplierReview = 2,
    AccountantReview = 3,
    SupplierSigning = 4,
    AccountantInitialing = 5,
    DirectorSigning = 6,
    Stamping = 7,
    Effective = 8,
    Liquidated = 9,
    Cancelled = 10,
    Returned = 11,
}

/// <summary>Input line used to (re)build the goods list of a draft contract.</summary>
public sealed record ContractItemDraft(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal? MaxUnitPrice);

/// <summary>Hợp đồng. Supplier users only see their own contracts (global query filter).</summary>
public sealed class Contract : Entity, ISupplierScoped
{
    private readonly List<ContractItem> _items = [];

    private Contract()
    {
        Number = string.Empty;
        Name = string.Empty;
    }

    public Contract(string number, string name, ProcurementType procurementType, ContractKind kind, Guid supplierId, Guid categoryId, Guid? bidDecisionId)
    {
        if (procurementType == ProcurementType.Bidding && bidDecisionId is null)
        {
            throw new DomainException("CONTRACT_BID_REQUIRED");
        }

        Number = Guard.Required(number, "CONTRACT_NUMBER_REQUIRED");
        Name = Guard.Required(name, "CONTRACT_NAME_REQUIRED");
        ProcurementType = procurementType;
        Kind = kind;
        SupplierId = supplierId;
        CategoryId = categoryId;
        BidDecisionId = procurementType == ProcurementType.Bidding ? bidDecisionId : null;
        RequiresWarehouseReceipt = true;
        Status = ContractStatus.Draft;
    }

    public string Number { get; private set; }

    public string Name { get; private set; }

    public ProcurementType ProcurementType { get; private set; }

    public ContractKind Kind { get; private set; }

    /// <summary>False for services/repairs: acceptance uses virtual receipts (phiếu xác nhận khối lượng).</summary>
    public bool RequiresWarehouseReceipt { get; private set; }

    public Guid? BidDecisionId { get; private set; }

    public Guid SupplierId { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>Cơ sở thực hiện (null = toàn bệnh viện).</summary>
    public Guid? FacilityId { get; private set; }

    public DateOnly? SignedDate { get; private set; }

    public DateOnly? EffectiveDate { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>Thời gian thực hiện hợp đồng (VD: "12 tháng kể từ ngày ký").</summary>
    public string? Duration { get; private set; }

    public decimal AmountBeforeVat { get; private set; }

    public decimal VatAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string? PaymentTerms { get; private set; }

    public string? WarrantyTerms { get; private set; }

    public string? DeliveryTerms { get; private set; }

    public string? GuaranteeNumber { get; private set; }

    public string? GuaranteeBank { get; private set; }

    public decimal? GuaranteeAmount { get; private set; }

    public DateOnly? GuaranteeExpiry { get; private set; }

    public string? Note { get; private set; }

    public Guid? TemplateId { get; private set; }

    /// <summary>Hồ sơ hợp đồng (DOCX/PDF versions, signatures).</summary>
    public Guid? DocumentId { get; private set; }

    public ContractStatus Status { get; private set; }

    public string? LastReturnReason { get; private set; }

    public DateTimeOffset? EffectiveAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<ContractItem> Items => _items;

    /// <summary>Content (header, terms, goods) may change only before the workflow starts or after it was returned.</summary>
    public bool IsEditable => Status is ContractStatus.Draft or ContractStatus.Returned;

    public bool IsEffective => Status == ContractStatus.Effective;

    public void UpdateHeader(string number, string name, ContractKind kind, bool requiresWarehouseReceipt, Guid categoryId, Guid? facilityId, string? note)
    {
        EnsureEditable();
        Number = Guard.Required(number, "CONTRACT_NUMBER_REQUIRED");
        Name = Guard.Required(name, "CONTRACT_NAME_REQUIRED");
        Kind = kind;
        RequiresWarehouseReceipt = requiresWarehouseReceipt;
        CategoryId = categoryId;
        FacilityId = facilityId;
        Note = Guard.Optional(note);
    }

    public void UpdateTerms(
        DateOnly? signedDate,
        DateOnly? effectiveDate,
        DateOnly? expiryDate,
        string? duration,
        string? paymentTerms,
        string? warrantyTerms,
        string? deliveryTerms,
        string? guaranteeNumber,
        string? guaranteeBank,
        decimal? guaranteeAmount,
        DateOnly? guaranteeExpiry)
    {
        EnsureEditable();
        if (effectiveDate is not null && expiryDate is not null && expiryDate < effectiveDate)
        {
            throw new DomainException("CONTRACT_DATES_INVALID");
        }

        if (guaranteeAmount is < 0)
        {
            throw new DomainException("CONTRACT_GUARANTEE_INVALID");
        }

        SignedDate = signedDate;
        EffectiveDate = effectiveDate;
        ExpiryDate = expiryDate;
        Duration = Guard.Optional(duration);
        PaymentTerms = Guard.Optional(paymentTerms);
        WarrantyTerms = Guard.Optional(warrantyTerms);
        DeliveryTerms = Guard.Optional(deliveryTerms);
        GuaranteeNumber = Guard.Optional(guaranteeNumber);
        GuaranteeBank = Guard.Optional(guaranteeBank);
        GuaranteeAmount = guaranteeAmount;
        GuaranteeExpiry = guaranteeExpiry;
    }

    /// <summary>Replaces the goods list. Unit prices may not exceed the winning price (spec §6.1 B2).</summary>
    public void SetItems(IReadOnlyCollection<ContractItemDraft> lines)
    {
        EnsureEditable();
        if (lines.Count == 0)
        {
            throw new DomainException("CONTRACT_ITEMS_REQUIRED");
        }

        if (lines.GroupBy(l => l.ItemId).Any(g => g.Count() > 1))
        {
            throw new DomainException("CONTRACT_ITEM_DUPLICATE");
        }

        _items.Clear();
        foreach (var line in lines)
        {
            _items.Add(new ContractItem(Id, line));
        }

        RecalculateTotals();
    }

    public void AttachDocument(Guid documentId, Guid? templateId)
    {
        DocumentId = documentId;
        TemplateId = templateId;
    }

    /// <summary>Mirrors the signing workflow onto the contract status (called whenever the workflow moves).</summary>
    public void ApplyWorkflow(WorkflowStatus workflowStatus, WorkflowStepInstance? activeStep, string? returnReason, DateTimeOffset now)
    {
        if (Status is ContractStatus.Liquidated or ContractStatus.Cancelled)
        {
            return;
        }

        switch (workflowStatus)
        {
            case WorkflowStatus.Completed:
                Status = ContractStatus.Effective;
                EffectiveAt ??= now;
                SignedDate ??= DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
                EffectiveDate ??= SignedDate;
                break;
            case WorkflowStatus.Returned:
                Status = ContractStatus.Returned;
                LastReturnReason = returnReason;
                break;
            case WorkflowStatus.Cancelled:
                Status = ContractStatus.Draft;
                break;
            case WorkflowStatus.Running when activeStep is not null:
                Status = StatusForStep(activeStep.Code, activeStep.ActionType);
                if (returnReason is not null)
                {
                    LastReturnReason = returnReason;
                }

                break;
        }
    }

    /// <summary>Maps a workflow step onto the contract status; custom steps fall back to their action type.</summary>
    public static ContractStatus StatusForStep(string stepCode, WorkflowActionType action) => stepCode switch
    {
        "ACCOUNTANT_REVIEW" => ContractStatus.AccountantReview,
        "ACCOUNTANT_INITIAL" => ContractStatus.AccountantInitialing,
        "DIRECTOR" => ContractStatus.DirectorSigning,
        "CLERK_SEAL" => ContractStatus.Stamping,
        _ => action switch
        {
            WorkflowActionType.SupplierReview => ContractStatus.SupplierReview,
            WorkflowActionType.SupplierSign => ContractStatus.SupplierSigning,
            WorkflowActionType.Initial => ContractStatus.AccountantInitialing,
            WorkflowActionType.Sign => ContractStatus.DirectorSigning,
            WorkflowActionType.OrgSeal => ContractStatus.Stamping,
            _ => ContractStatus.InReview,
        },
    };

    public void Cancel(DateTimeOffset now)
    {
        if (Status is ContractStatus.Effective or ContractStatus.Liquidated)
        {
            throw new DomainException("CONTRACT_NOT_CANCELLABLE");
        }

        Status = ContractStatus.Cancelled;
        CancelledAt = now;
    }

    public DateTimeOffset? LiquidatedAt { get; private set; }

    /// <summary>Hồ sơ lưu trữ được xác nhận "đã đầy đủ" (spec §9.1-12).</summary>
    public bool IsArchiveComplete { get; private set; }

    public DateTimeOffset? ArchiveCompletedAt { get; private set; }

    public string? ArchiveCompletedByName { get; private set; }

    public void SetArchiveComplete(bool complete, string? userName, DateTimeOffset now)
    {
        if (complete && Status is ContractStatus.Draft or ContractStatus.Cancelled)
        {
            throw new DomainException("ARCHIVE_CONTRACT_NOT_SIGNED");
        }

        IsArchiveComplete = complete;
        ArchiveCompletedAt = complete ? now : null;
        ArchiveCompletedByName = complete ? userName : null;
    }

    /// <summary>Biên bản thanh lý đã ký xong: the contract is closed and no new business may arise (spec §5.6).</summary>
    public void Liquidate(DateTimeOffset now)
    {
        if (Status == ContractStatus.Liquidated)
        {
            return;
        }

        if (Status != ContractStatus.Effective)
        {
            throw new DomainException("CONTRACT_NOT_EFFECTIVE");
        }

        Status = ContractStatus.Liquidated;
        LiquidatedAt = now;
    }

    /// <summary>Applies an effective appendix: quantities, prices or expiry change (spec §5.3).</summary>
    public void ApplyAppendix(ContractAppendix appendix)
    {
        if (Status != ContractStatus.Effective)
        {
            throw new DomainException("CONTRACT_NOT_EFFECTIVE");
        }

        if (appendix.ContractId != Id)
        {
            throw new DomainException("APPENDIX_CONTRACT_MISMATCH");
        }

        foreach (var line in appendix.Lines)
        {
            var item = _items.FirstOrDefault(i => i.Id == line.ContractItemId) ?? throw new DomainException("APPENDIX_ITEM_NOT_FOUND");
            item.Adjust(line.NewQuantity, line.NewUnitPrice);
        }

        if (appendix.NewExpiryDate is { } expiry)
        {
            ExpiryDate = expiry;
        }

        RecalculateTotals();
    }

    public void RecalculateTotals()
    {
        // Unit prices include VAT (đơn giá đã gồm thuế GTGT — ADR-028): the VAT is extracted, not added.
        TotalAmount = _items.Sum(i => i.Amount);
        VatAmount = _items.Sum(i => i.VatAmount);
        AmountBeforeVat = TotalAmount - VatAmount;
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new DomainException("CONTRACT_LOCKED");
        }
    }
}

/// <summary>
/// Mặt hàng của hợp đồng with execution tracking. Item data is copied so the contract keeps what was signed.
/// Còn lại = SL hợp đồng − SL đã nhập kho (DECISIONS.md ADR-029).
/// </summary>
public sealed class ContractItem
{
    private ContractItem()
    {
        ItemCode = string.Empty;
        ItemName = string.Empty;
        Unit = string.Empty;
    }

    internal ContractItem(Guid contractId, ContractItemDraft line)
    {
        if (line.Quantity <= 0)
        {
            throw new DomainException("CONTRACT_QUANTITY_INVALID");
        }

        if (line.UnitPrice < 0)
        {
            throw new DomainException("CONTRACT_PRICE_INVALID");
        }

        if (line.MaxUnitPrice is { } max && line.UnitPrice > max)
        {
            throw new DomainException("CONTRACT_PRICE_EXCEEDS_BID");
        }

        if (line.VatRate is < 0 or > 100)
        {
            throw new DomainException("CONTRACT_VAT_INVALID");
        }

        Id = Guid.CreateVersion7();
        ContractId = contractId;
        ItemId = line.ItemId;
        ItemCode = line.ItemCode;
        ItemName = line.ItemName;
        Unit = line.Unit;
        Quantity = line.Quantity;
        UnitPrice = line.UnitPrice;
        VatRate = line.VatRate;
        MaxUnitPrice = line.MaxUnitPrice;
        Recalculate();
    }

    public Guid Id { get; private set; }

    public Guid ContractId { get; private set; }

    public Guid ItemId { get; private set; }

    public string ItemCode { get; private set; }

    public string ItemName { get; private set; }

    public string Unit { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Đơn giá đã gồm thuế GTGT.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Thuế suất GTGT (%).</summary>
    public decimal VatRate { get; private set; }

    /// <summary>Thành tiền (đã gồm thuế GTGT) = số lượng × đơn giá.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Thuế GTGT nằm trong thành tiền.</summary>
    public decimal VatAmount { get; private set; }

    /// <summary>Winning unit price at the time the contract was drafted (null outside bids).</summary>
    public decimal? MaxUnitPrice { get; private set; }

    public decimal ProposedQuantity { get; private set; }

    public decimal ApprovedQuantity { get; private set; }

    public decimal CalledQuantity { get; private set; }

    public decimal ReceivedQuantity { get; private set; }

    public decimal AcceptedQuantity { get; private set; }

    public decimal PaidQuantity { get; private set; }

    public decimal RemainingQuantity => Quantity - ReceivedQuantity;

    /// <summary>Execution trackers are maintained by the proposal, call-order, receipt and payment modules.</summary>
    public void Track(decimal proposed, decimal approved, decimal called, decimal received, decimal accepted, decimal paid)
    {
        ProposedQuantity = proposed;
        ApprovedQuantity = approved;
        CalledQuantity = called;
        ReceivedQuantity = received;
        AcceptedQuantity = accepted;
        PaidQuantity = paid;
    }

    internal void Adjust(decimal? newQuantity, decimal? newUnitPrice)
    {
        if (newQuantity is { } q)
        {
            if (q <= 0 || q < ReceivedQuantity)
            {
                throw new DomainException("APPENDIX_QUANTITY_BELOW_RECEIVED");
            }

            Quantity = q;
        }

        if (newUnitPrice is { } p)
        {
            if (p < 0)
            {
                throw new DomainException("CONTRACT_PRICE_INVALID");
            }

            UnitPrice = p;
        }

        Recalculate();
    }

    private void Recalculate()
    {
        Amount = decimal.Round(Quantity * UnitPrice, 0, MidpointRounding.AwayFromZero);
        VatAmount = decimal.Round(Amount * VatRate / (100m + VatRate), 0, MidpointRounding.AwayFromZero);
    }
}
