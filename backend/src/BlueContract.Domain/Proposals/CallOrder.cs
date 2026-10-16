using BlueContract.Domain.Common;

namespace BlueContract.Domain.Proposals;

public enum CallOrderStatus
{
    /// <summary>Đã gửi NCC, chờ xác nhận.</summary>
    Sent = 0,
    SupplierConfirmed = 1,
    PartiallyConfirmed = 2,
    Rejected = 3,
    Delivered = 4,
    Cancelled = 5,
}

public sealed record CallOrderLineDraft(
    Guid SummaryLineId,
    Guid ContractId,
    string ContractNumber,
    Guid ContractItemId,
    string ItemCode,
    string ItemName,
    string Unit,
    decimal UnitPrice,
    decimal Quantity);

public sealed record CallConfirmation(Guid LineId, decimal ConfirmedQuantity, string? ShortageReason);

/// <summary>
/// Lần gọi hàng (spec §5.4, §6.3): one supplier per order (split automatically). The supplier confirms each line with a
/// quantity ≤ the called quantity; a lower quantity requires a reason.
/// </summary>
public sealed class CallOrder : Entity, ISupplierScoped
{
    private readonly List<CallOrderLine> _lines = [];

    private CallOrder()
    {
        Number = string.Empty;
    }

    public CallOrder(string number, Guid summaryId, Guid supplierId, Guid warehouseId, DateOnly callDate, DateOnly deliveryDeadline, string? contactName, string? note)
    {
        if (deliveryDeadline < callDate)
        {
            throw new DomainException("CALL_DEADLINE_INVALID");
        }

        Number = Guard.Required(number, "CALL_NUMBER_REQUIRED");
        SummaryId = summaryId;
        SupplierId = supplierId;
        WarehouseId = warehouseId;
        CallDate = callDate;
        DeliveryDeadline = deliveryDeadline;
        ContactName = Guard.Optional(contactName);
        Note = Guard.Optional(note);
        Status = CallOrderStatus.Sent;
    }

    public string Number { get; private set; }

    public Guid SummaryId { get; private set; }

    public Guid SupplierId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public DateOnly CallDate { get; private set; }

    public DateOnly DeliveryDeadline { get; private set; }

    public string? ContactName { get; private set; }

    public string? Note { get; private set; }

    public CallOrderStatus Status { get; private set; }

    public Guid? DocumentId { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public string? ConfirmedByName { get; private set; }

    public string? SupplierNote { get; private set; }

    public IReadOnlyCollection<CallOrderLine> Lines => _lines;

    public decimal TotalAmount => _lines.Sum(l => l.Amount);

    public void AddLines(IReadOnlyCollection<CallOrderLineDraft> lines)
    {
        if (lines.Count == 0)
        {
            throw new DomainException("CALL_LINES_REQUIRED");
        }

        foreach (var line in lines)
        {
            _lines.Add(new CallOrderLine(Id, line));
        }
    }

    public void AttachDocument(Guid documentId) => DocumentId = documentId;

    /// <summary>NCC xác nhận: every line must be answered; returns the quantity not confirmed per summary line.</summary>
    public IReadOnlyList<(Guid SummaryLineId, decimal Released)> Confirm(IReadOnlyCollection<CallConfirmation> confirmations, Guid userId, string? userName, string? note, DateTimeOffset now)
    {
        if (Status != CallOrderStatus.Sent)
        {
            throw new DomainException("CALL_NOT_AWAITING_CONFIRMATION");
        }

        if (confirmations.Count != _lines.Count || _lines.Any(l => confirmations.All(c => c.LineId != l.Id)))
        {
            throw new DomainException("CALL_CONFIRMATION_INCOMPLETE");
        }

        var released = new List<(Guid, decimal)>();
        foreach (var line in _lines)
        {
            var c = confirmations.First(x => x.LineId == line.Id);
            line.Confirm(c.ConfirmedQuantity, c.ShortageReason);
            if (line.Quantity > c.ConfirmedQuantity)
            {
                released.Add((line.SummaryLineId, line.Quantity - c.ConfirmedQuantity));
            }
        }

        Status = _lines.All(l => l.ConfirmedQuantity == 0) ? CallOrderStatus.Rejected
            : _lines.All(l => l.ConfirmedQuantity == l.Quantity) ? CallOrderStatus.SupplierConfirmed
            : CallOrderStatus.PartiallyConfirmed;
        ConfirmedAt = now;
        ConfirmedBy = userId;
        ConfirmedByName = userName;
        SupplierNote = Guard.Optional(note);
        return released;
    }

    /// <summary>Hủy lần gọi (chỉ khi NCC chưa xác nhận); returns the quantities to give back to the summary.</summary>
    public IReadOnlyList<(Guid SummaryLineId, decimal Released)> Cancel()
    {
        if (Status != CallOrderStatus.Sent)
        {
            throw new DomainException("CALL_NOT_CANCELLABLE");
        }

        Status = CallOrderStatus.Cancelled;
        return [.. _lines.Select(l => (l.SummaryLineId, l.Quantity))];
    }

    /// <summary>Called by the warehouse receipt module when goods arrive.</summary>
    public void RecordDelivery(Guid contractItemId, decimal quantity)
    {
        var line = _lines.FirstOrDefault(l => l.ContractItemId == contractItemId);
        if (line is null)
        {
            return;
        }

        line.Deliver(quantity);
        RefreshDeliveryStatus();
    }

    /// <summary>
    /// Sets the delivered quantity of an item from the receipts linked to this order (recomputed, so re-sent or cancelled
    /// receipts never double count). A fully delivered order becomes Delivered; a cancelled receipt can reopen it.
    /// </summary>
    public void SetDelivered(Guid contractItemId, decimal quantity)
    {
        var line = _lines.FirstOrDefault(l => l.ContractItemId == contractItemId);
        if (line is null)
        {
            return;
        }

        line.SetDelivered(Math.Max(0, quantity));
        RefreshDeliveryStatus();
    }

    private void RefreshDeliveryStatus()
    {
        var complete = _lines.All(l => l.DeliveredQuantity >= (l.ConfirmedQuantity ?? l.Quantity));
        if (complete && Status is CallOrderStatus.SupplierConfirmed or CallOrderStatus.PartiallyConfirmed)
        {
            Status = CallOrderStatus.Delivered;
        }
        else if (!complete && Status == CallOrderStatus.Delivered)
        {
            Status = _lines.All(l => l.ConfirmedQuantity == l.Quantity) ? CallOrderStatus.SupplierConfirmed : CallOrderStatus.PartiallyConfirmed;
        }
    }

    public bool IsOverdue(DateOnly today) => Status == CallOrderStatus.Sent && DeliveryDeadline < today;
}

public sealed class CallOrderLine
{
    private CallOrderLine()
    {
        ContractNumber = string.Empty;
        ItemCode = string.Empty;
        ItemName = string.Empty;
        Unit = string.Empty;
    }

    internal CallOrderLine(Guid orderId, CallOrderLineDraft d)
    {
        if (d.Quantity <= 0)
        {
            throw new DomainException("CALL_QUANTITY_INVALID");
        }

        Id = Guid.CreateVersion7();
        CallOrderId = orderId;
        SummaryLineId = d.SummaryLineId;
        ContractId = d.ContractId;
        ContractNumber = d.ContractNumber;
        ContractItemId = d.ContractItemId;
        ItemCode = d.ItemCode;
        ItemName = d.ItemName;
        Unit = d.Unit;
        UnitPrice = d.UnitPrice;
        Quantity = d.Quantity;
    }

    public Guid Id { get; private set; }

    public Guid CallOrderId { get; private set; }

    public Guid SummaryLineId { get; private set; }

    public Guid ContractId { get; private set; }

    public string ContractNumber { get; private set; }

    public Guid ContractItemId { get; private set; }

    public string ItemCode { get; private set; }

    public string ItemName { get; private set; }

    public string Unit { get; private set; }

    public decimal UnitPrice { get; private set; }

    /// <summary>SL gọi.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>SL NCC xác nhận (≤ SL gọi).</summary>
    public decimal? ConfirmedQuantity { get; private set; }

    public string? ShortageReason { get; private set; }

    public decimal DeliveredQuantity { get; private set; }

    public decimal Amount => decimal.Round((ConfirmedQuantity ?? Quantity) * UnitPrice, 0, MidpointRounding.AwayFromZero);

    internal void Confirm(decimal confirmed, string? reason)
    {
        if (confirmed < 0 || confirmed > Quantity)
        {
            throw new DomainException("CALL_CONFIRM_EXCEEDS_CALLED");
        }

        if (confirmed < Quantity && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("CALL_SHORTAGE_REASON_REQUIRED");
        }

        ConfirmedQuantity = confirmed;
        ShortageReason = confirmed < Quantity ? reason!.Trim() : null;
    }

    internal void Deliver(decimal quantity) => DeliveredQuantity += quantity;

    internal void SetDelivered(decimal quantity) => DeliveredQuantity = quantity;
}
