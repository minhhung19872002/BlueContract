using BlueContract.Domain.Common;
using BlueContract.Domain.Workflows;

namespace BlueContract.Domain.Receipts;

public enum ReceiptType
{
    /// <summary>Phiếu nhập kho.</summary>
    Receipt = 0,

    /// <summary>Phiếu hoàn trả (giá trị âm trong lũy kế).</summary>
    Return = 1,
}

public enum ReceiptSource
{
    /// <summary>Pushed or pulled from the HIS (idempotent by HisReceiptId).</summary>
    His = 0,

    /// <summary>Lập trên cổng.</summary>
    Portal = 1,

    /// <summary>Phiếu "ảo" (xác nhận khối lượng) for contracts without warehouse receipts — completed without signing.</summary>
    VirtualPortal = 2,
}

public enum ReceiptStatus
{
    /// <summary>Lập trên cổng, chưa gửi ký.</summary>
    Draft = 0,

    /// <summary>Đang trong luồng 5 chữ ký.</summary>
    Signing = 1,

    /// <summary>Bị trả lại (chờ HIS gửi lại / bổ sung rồi gửi ký lại).</summary>
    Returned = 2,

    /// <summary>Đã ký đủ (hoặc phiếu ảo đã xác nhận).</summary>
    Completed = 3,

    Cancelled = 4,
}

public enum ReceiptValidationStatus
{
    Valid = 0,
    Warning = 1,
    Invalid = 2,
}

/// <summary>Kết quả của một rule trong validation engine (spec §5.5).</summary>
public sealed record ReceiptIssue(string Code, ReceiptValidationStatus Severity, string Message, int? LineNo = null);

public sealed record ReceiptLineDraft(
    string? HisKey,
    string ItemCode,
    string ItemName,
    string Unit,
    string? LotNumber,
    DateOnly? ExpiryDate,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate);

/// <summary>Thông tin hóa đơn (bắt buộc trước khi người giao hàng ký).</summary>
public sealed record InvoiceInfo(
    string Symbol,
    string Number,
    DateOnly Date,
    decimal? AmountBeforeVat,
    decimal? VatAmount,
    decimal TotalAmount,
    string? SellerTaxCode);

/// <summary>
/// Phiếu nhập kho / Phiếu hoàn trả (spec §5.5, §6.4). Linked to a contract by (Số QĐ + MST NCC); the supplier may be unknown
/// when the HIS sends a tax code the portal does not know (the receipt is then Invalid and only visible to the hospital).
/// </summary>
public sealed class WarehouseReceipt : Entity
{
    private readonly List<WarehouseReceiptLine> _lines = [];
    private List<ReceiptIssue> _issues = [];

    private WarehouseReceipt()
    {
        Number = string.Empty;
        SupplierTaxCode = string.Empty;
        WarehouseCode = string.Empty;
    }

    public WarehouseReceipt(
        string number,
        ReceiptType type,
        ReceiptSource source,
        string? hisReceiptId,
        DateOnly receiptDate,
        string warehouseCode,
        Guid? warehouseId,
        string supplierTaxCode,
        string? bidDecisionNumber)
    {
        if (source == ReceiptSource.His && string.IsNullOrWhiteSpace(hisReceiptId))
        {
            throw new DomainException("RECEIPT_HIS_ID_REQUIRED");
        }

        Number = Guard.Required(number, "RECEIPT_NUMBER_REQUIRED");
        Type = type;
        Source = source;
        HisReceiptId = source == ReceiptSource.His ? hisReceiptId!.Trim() : null;
        ReceiptDate = receiptDate;
        WarehouseCode = Guard.Required(warehouseCode, "RECEIPT_WAREHOUSE_REQUIRED");
        WarehouseId = warehouseId;
        SupplierTaxCode = Guard.Required(supplierTaxCode, "RECEIPT_TAX_CODE_REQUIRED");
        BidDecisionNumber = Guard.Optional(bidDecisionNumber);
        Status = ReceiptStatus.Draft;
    }

    /// <summary>Số phiếu (của HIS hoặc do cổng cấp).</summary>
    public string Number { get; private set; }

    public ReceiptType Type { get; private set; }

    public ReceiptSource Source { get; private set; }

    public string? HisReceiptId { get; private set; }

    /// <summary>SHA-256 of the last HIS payload: an identical resend is a no-op (idempotent).</summary>
    public string? HisPayloadHash { get; private set; }

    public DateOnly ReceiptDate { get; private set; }

    public string WarehouseCode { get; private set; }

    public Guid? WarehouseId { get; private set; }

    public Guid? SupplierId { get; private set; }

    public string SupplierTaxCode { get; private set; }

    public string? BidDecisionNumber { get; private set; }

    public Guid? ContractId { get; private set; }

    public string? ContractNumber { get; private set; }

    public Guid? CallOrderId { get; private set; }

    /// <summary>Người giao hàng (nhân viên NCC).</summary>
    public string? DelivererName { get; private set; }

    public string? Note { get; private set; }

    public string? InvoiceSymbol { get; private set; }

    public string? InvoiceNumber { get; private set; }

    public DateOnly? InvoiceDate { get; private set; }

    public decimal? InvoiceAmountBeforeVat { get; private set; }

    public decimal? InvoiceVatAmount { get; private set; }

    public decimal? InvoiceTotalAmount { get; private set; }

    public string? InvoiceSellerTaxCode { get; private set; }

    public string? InvoiceXmlFileKey { get; private set; }

    public string? InvoicePdfFileKey { get; private set; }

    public DateTimeOffset? InvoiceUpdatedAt { get; private set; }

    public string? InvoiceUpdatedByName { get; private set; }

    public ReceiptStatus Status { get; private set; }

    public ReceiptValidationStatus ValidationStatus { get; private set; }

    public DateTimeOffset? ValidatedAt { get; private set; }

    /// <summary>Số cảnh báo/lỗi (queryable copy of <see cref="Issues"/>).</summary>
    public int IssueCount { get; private set; }

    public Guid? DocumentId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? LastReturnReason { get; private set; }

    /// <summary>Đã đưa vào biên bản nghiệm thu (Phase 7).</summary>
    public bool IsIncludedInAcceptance { get; private set; }

    public Guid? AcceptanceId { get; private set; }

    public IReadOnlyCollection<WarehouseReceiptLine> Lines => _lines;

    public IReadOnlyList<ReceiptIssue> Issues => _issues;

    public bool HasInvoice => InvoiceNumber is not null && InvoiceSymbol is not null && InvoiceDate is not null && InvoiceTotalAmount is not null;

    public bool IsVirtual => Source == ReceiptSource.VirtualPortal;

    /// <summary>Thành tiền (đã gồm VAT), always positive; see <see cref="SignedAmount"/>.</summary>
    public decimal TotalAmount => _lines.Sum(l => l.Amount);

    public decimal VatAmount => _lines.Sum(l => l.VatAmount);

    /// <summary>Giá trị trong lũy kế: hoàn trả mang dấu âm.</summary>
    public decimal SignedAmount => Type == ReceiptType.Return ? -TotalAmount : TotalAmount;

    /// <summary>Lines, contract mapping and the HIS payload may change until the first signature (service checks signatures).</summary>
    public bool IsEditable => Status is ReceiptStatus.Draft or ReceiptStatus.Returned or ReceiptStatus.Signing;

    /// <summary>Counts toward "đã nhập kho" of the contract (goods are physically in or out of the warehouse).</summary>
    public bool CountsAsReceived => Status is ReceiptStatus.Signing or ReceiptStatus.Returned or ReceiptStatus.Completed
        || (Status == ReceiptStatus.Draft && Source == ReceiptSource.His);

    public void UpdateHeader(string number, DateOnly receiptDate, string warehouseCode, Guid? warehouseId, string supplierTaxCode, string? bidDecisionNumber,
        string? delivererName, string? note)
    {
        EnsureEditable();
        Number = Guard.Required(number, "RECEIPT_NUMBER_REQUIRED");
        ReceiptDate = receiptDate;
        WarehouseCode = Guard.Required(warehouseCode, "RECEIPT_WAREHOUSE_REQUIRED");
        WarehouseId = warehouseId;
        SupplierTaxCode = Guard.Required(supplierTaxCode, "RECEIPT_TAX_CODE_REQUIRED");
        BidDecisionNumber = Guard.Optional(bidDecisionNumber);
        DelivererName = Guard.Optional(delivererName);
        Note = Guard.Optional(note);
    }

    public void SetHisPayloadHash(string hash) => HisPayloadHash = hash;

    public void SetLines(IReadOnlyCollection<ReceiptLineDraft> lines)
    {
        EnsureEditable();
        if (lines.Count == 0)
        {
            throw new DomainException("RECEIPT_LINES_REQUIRED");
        }

        _lines.Clear();
        var no = 1;
        foreach (var line in lines)
        {
            _lines.Add(new WarehouseReceiptLine(Id, no++, line));
        }
    }

    /// <summary>Result of mapping (Số QĐ + MST) onto the portal's supplier and effective contract; null when not found.</summary>
    public void MapParties(Guid? supplierId, Guid? contractId, string? contractNumber, Guid? callOrderId)
    {
        SupplierId = supplierId;
        ContractId = contractId;
        ContractNumber = contractId is null ? null : contractNumber;
        CallOrderId = contractId is null ? null : callOrderId;
        if (contractId is null)
        {
            foreach (var line in _lines)
            {
                line.Map(null, null);
            }
        }
    }

    public void MapLine(int lineNo, Guid? itemId, Guid? contractItemId) =>
        (_lines.FirstOrDefault(l => l.LineNo == lineNo) ?? throw new DomainException("RECEIPT_LINE_NOT_FOUND")).Map(itemId, contractItemId);

    public void SetInvoice(InvoiceInfo invoice, string? userName, DateTimeOffset now)
    {
        if (Status is ReceiptStatus.Completed or ReceiptStatus.Cancelled)
        {
            throw new DomainException("RECEIPT_LOCKED");
        }

        if (invoice.TotalAmount <= 0)
        {
            throw new DomainException("INVOICE_TOTAL_INVALID");
        }

        InvoiceSymbol = Guard.Required(invoice.Symbol, "INVOICE_SYMBOL_REQUIRED").ToUpperInvariant();
        InvoiceNumber = Guard.Required(invoice.Number, "INVOICE_NUMBER_REQUIRED");
        InvoiceDate = invoice.Date;
        InvoiceAmountBeforeVat = invoice.AmountBeforeVat;
        InvoiceVatAmount = invoice.VatAmount;
        InvoiceTotalAmount = invoice.TotalAmount;
        InvoiceSellerTaxCode = Guard.Optional(invoice.SellerTaxCode);
        InvoiceUpdatedAt = now;
        InvoiceUpdatedByName = userName;
    }

    public void AttachInvoiceFiles(string? xmlFileKey, string? pdfFileKey)
    {
        if (Status is ReceiptStatus.Completed or ReceiptStatus.Cancelled)
        {
            throw new DomainException("RECEIPT_LOCKED");
        }

        InvoiceXmlFileKey = xmlFileKey ?? InvoiceXmlFileKey;
        InvoicePdfFileKey = pdfFileKey ?? InvoicePdfFileKey;
    }

    /// <summary>Stores the validation engine output; the overall status is the most severe issue.</summary>
    public void ApplyValidation(IReadOnlyCollection<ReceiptIssue> issues, DateTimeOffset now)
    {
        _issues = [.. issues.OrderByDescending(i => i.Severity).ThenBy(i => i.LineNo ?? 0)];
        ValidationStatus = _issues.Count == 0 ? ReceiptValidationStatus.Valid : _issues.Max(i => i.Severity);
        IssueCount = _issues.Count;
        ValidatedAt = now;
    }

    public void AttachDocument(Guid documentId) => DocumentId = documentId;

    /// <summary>Mirrors the 5-signature workflow onto the receipt.</summary>
    public void ApplyWorkflow(WorkflowStatus workflowStatus, string? returnReason, DateTimeOffset now)
    {
        if (Status == ReceiptStatus.Cancelled)
        {
            return;
        }

        switch (workflowStatus)
        {
            case WorkflowStatus.Running:
                Status = ReceiptStatus.Signing;
                break;
            case WorkflowStatus.Returned:
                Status = ReceiptStatus.Returned;
                LastReturnReason = returnReason;
                break;
            case WorkflowStatus.Completed:
                Status = ReceiptStatus.Completed;
                CompletedAt ??= now;
                break;
            case WorkflowStatus.Cancelled:
                Status = ReceiptStatus.Draft;
                break;
        }
    }

    /// <summary>Phiếu ảo: hoàn thành không cần ký (spec §6.4) — only when no line is Invalid.</summary>
    public void CompleteVirtual(DateTimeOffset now)
    {
        if (!IsVirtual)
        {
            throw new DomainException("RECEIPT_NOT_VIRTUAL");
        }

        if (Status != ReceiptStatus.Draft)
        {
            throw new DomainException("RECEIPT_LOCKED");
        }

        if (ValidationStatus == ReceiptValidationStatus.Invalid)
        {
            throw new DomainException("RECEIPT_INVALID_BLOCKS_SIGNING");
        }

        Status = ReceiptStatus.Completed;
        CompletedAt = now;
    }

    public void Cancel()
    {
        if (Status == ReceiptStatus.Completed || IsIncludedInAcceptance)
        {
            throw new DomainException("RECEIPT_NOT_CANCELLABLE");
        }

        Status = ReceiptStatus.Cancelled;
    }

    /// <summary>Đưa vào biên bản nghiệm thu (only signed receipts not yet accepted).</summary>
    public void IncludeInAcceptance(Guid acceptanceId)
    {
        if (Status != ReceiptStatus.Completed)
        {
            throw new DomainException("RECEIPT_NOT_COMPLETED");
        }

        if (IsIncludedInAcceptance && AcceptanceId != acceptanceId)
        {
            throw new DomainException("RECEIPT_ALREADY_ACCEPTED");
        }

        IsIncludedInAcceptance = true;
        AcceptanceId = acceptanceId;
    }

    public void ReleaseFromAcceptance(Guid acceptanceId)
    {
        if (AcceptanceId == acceptanceId)
        {
            IsIncludedInAcceptance = false;
            AcceptanceId = null;
        }
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new DomainException("RECEIPT_LOCKED");
        }
    }
}

public sealed class WarehouseReceiptLine
{
    private WarehouseReceiptLine()
    {
        ItemCode = string.Empty;
        ItemName = string.Empty;
        Unit = string.Empty;
    }

    internal WarehouseReceiptLine(Guid receiptId, int lineNo, ReceiptLineDraft d)
    {
        if (d.Quantity <= 0)
        {
            throw new DomainException("RECEIPT_QUANTITY_INVALID");
        }

        if (d.UnitPrice < 0)
        {
            throw new DomainException("RECEIPT_PRICE_INVALID");
        }

        if (d.VatRate is < 0 or > 100)
        {
            throw new DomainException("CONTRACT_VAT_INVALID");
        }

        Id = Guid.CreateVersion7();
        ReceiptId = receiptId;
        LineNo = lineNo;
        HisKey = Guard.Optional(d.HisKey);
        ItemCode = Guard.Required(d.ItemCode, "RECEIPT_ITEM_REQUIRED");
        ItemName = Guard.Required(d.ItemName, "RECEIPT_ITEM_REQUIRED");
        Unit = d.Unit?.Trim() ?? string.Empty;
        LotNumber = Guard.Optional(d.LotNumber);
        ExpiryDate = d.ExpiryDate;
        Quantity = d.Quantity;
        UnitPrice = d.UnitPrice;
        VatRate = d.VatRate;

        // Đơn giá đã gồm VAT (ADR-028): VAT is extracted from the amount.
        Amount = decimal.Round(Quantity * UnitPrice, 0, MidpointRounding.AwayFromZero);
        VatAmount = decimal.Round(Amount * VatRate / (100m + VatRate), 0, MidpointRounding.AwayFromZero);
    }

    public Guid Id { get; private set; }

    public Guid ReceiptId { get; private set; }

    public int LineNo { get; private set; }

    public string? HisKey { get; private set; }

    public Guid? ItemId { get; private set; }

    public Guid? ContractItemId { get; private set; }

    public string ItemCode { get; private set; }

    public string ItemName { get; private set; }

    public string Unit { get; private set; }

    public string? LotNumber { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Đơn giá đã gồm thuế GTGT.</summary>
    public decimal UnitPrice { get; private set; }

    public decimal VatRate { get; private set; }

    public decimal Amount { get; private set; }

    public decimal VatAmount { get; private set; }

    internal void Map(Guid? itemId, Guid? contractItemId)
    {
        ItemId = itemId;
        ContractItemId = contractItemId;
    }
}
