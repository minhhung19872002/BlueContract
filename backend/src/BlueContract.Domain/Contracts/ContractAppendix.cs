using BlueContract.Domain.Common;
using BlueContract.Domain.Workflows;

namespace BlueContract.Domain.Contracts;

public enum AppendixType
{
    /// <summary>Bổ sung số lượng.</summary>
    AddQuantity = 0,

    /// <summary>Giảm giá.</summary>
    PriceReduction = 1,

    /// <summary>Thay đổi thông tin.</summary>
    InfoChange = 2,

    /// <summary>Gia hạn thời gian thực hiện.</summary>
    Extension = 3,
}

public enum AppendixStatus
{
    Draft = 0,
    InWorkflow = 1,
    Effective = 2,
    Cancelled = 3,
}

/// <summary>Structured change of one contract item (old → new), kept for the history of the contract.</summary>
public sealed record AppendixLineDraft(Guid ContractItemId, decimal? NewQuantity, decimal? NewUnitPrice);

/// <summary>Phụ lục hợp đồng: goes through the same signing workflow as the contract; applied when effective.</summary>
public sealed class ContractAppendix : Entity, ISupplierScoped
{
    private readonly List<ContractAppendixLine> _lines = [];

    private ContractAppendix()
    {
        Number = string.Empty;
        Reason = string.Empty;
    }

    public ContractAppendix(Contract contract, string number, AppendixType type, string reason)
    {
        if (!contract.IsEffective)
        {
            throw new DomainException("CONTRACT_NOT_EFFECTIVE");
        }

        ContractId = contract.Id;
        SupplierId = contract.SupplierId;
        Number = Guard.Required(number, "APPENDIX_NUMBER_REQUIRED");
        Type = type;
        Reason = Guard.Required(reason, "APPENDIX_REASON_REQUIRED");
        Status = AppendixStatus.Draft;
    }

    public Guid ContractId { get; private set; }

    public Guid SupplierId { get; private set; }

    public string Number { get; private set; }

    public AppendixType Type { get; private set; }

    public string Reason { get; private set; }

    /// <summary>Free-text changes for "thay đổi thông tin" (bank account, representative...).</summary>
    public string? InfoChanges { get; private set; }

    public DateOnly? OldExpiryDate { get; private set; }

    public DateOnly? NewExpiryDate { get; private set; }

    public Guid? DocumentId { get; private set; }

    public AppendixStatus Status { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

    public decimal ValueChange { get; private set; }

    public IReadOnlyCollection<ContractAppendixLine> Lines => _lines;

    public void SetContent(Contract contract, string reason, string? infoChanges, DateOnly? newExpiryDate, IReadOnlyCollection<AppendixLineDraft> lines)
    {
        if (Status != AppendixStatus.Draft)
        {
            throw new DomainException("APPENDIX_LOCKED");
        }

        Reason = Guard.Required(reason, "APPENDIX_REASON_REQUIRED");
        InfoChanges = Guard.Optional(infoChanges);
        OldExpiryDate = contract.ExpiryDate;
        NewExpiryDate = newExpiryDate;

        switch (Type)
        {
            case AppendixType.Extension when newExpiryDate is null || (contract.ExpiryDate is { } old && newExpiryDate <= old):
                throw new DomainException("APPENDIX_EXPIRY_INVALID");
            case AppendixType.InfoChange when InfoChanges is null:
                throw new DomainException("APPENDIX_INFO_REQUIRED");
            case AppendixType.AddQuantity or AppendixType.PriceReduction when lines.Count == 0:
                throw new DomainException("APPENDIX_LINES_REQUIRED");
        }

        _lines.Clear();
        decimal change = 0;
        foreach (var line in lines)
        {
            var item = contract.Items.FirstOrDefault(i => i.Id == line.ContractItemId) ?? throw new DomainException("APPENDIX_ITEM_NOT_FOUND");
            if (Type == AppendixType.AddQuantity && (line.NewQuantity is null || line.NewQuantity <= item.Quantity))
            {
                throw new DomainException("APPENDIX_QUANTITY_NOT_INCREASED");
            }

            if (Type == AppendixType.PriceReduction && (line.NewUnitPrice is null || line.NewUnitPrice >= item.UnitPrice))
            {
                throw new DomainException("APPENDIX_PRICE_NOT_REDUCED");
            }

            if (line.NewQuantity is { } q && q < item.ReceivedQuantity)
            {
                throw new DomainException("APPENDIX_QUANTITY_BELOW_RECEIVED");
            }

            var row = new ContractAppendixLine(Id, item, line.NewQuantity, line.NewUnitPrice);
            _lines.Add(row);
            change += row.ValueChange;
        }

        ValueChange = change;
    }

    public void AttachDocument(Guid documentId) => DocumentId = documentId;

    public void ApplyWorkflow(WorkflowStatus status)
    {
        Status = status switch
        {
            WorkflowStatus.Running => AppendixStatus.InWorkflow,
            WorkflowStatus.Returned or WorkflowStatus.Cancelled => AppendixStatus.Draft,
            _ => Status,
        };
    }

    /// <summary>Called once the appendix is fully signed: updates the contract items / expiry.</summary>
    public void Apply(Contract contract, DateTimeOffset now)
    {
        if (Status == AppendixStatus.Effective)
        {
            return;
        }

        contract.ApplyAppendix(this);
        Status = AppendixStatus.Effective;
        AppliedAt = now;
    }

    public void Cancel()
    {
        if (Status == AppendixStatus.Effective)
        {
            throw new DomainException("APPENDIX_ALREADY_EFFECTIVE");
        }

        Status = AppendixStatus.Cancelled;
    }
}

public sealed class ContractAppendixLine
{
    private ContractAppendixLine()
    {
        ItemName = string.Empty;
    }

    internal ContractAppendixLine(Guid appendixId, ContractItem item, decimal? newQuantity, decimal? newUnitPrice)
    {
        Id = Guid.CreateVersion7();
        AppendixId = appendixId;
        ContractItemId = item.Id;
        ItemName = item.ItemName;
        OldQuantity = item.Quantity;
        NewQuantity = newQuantity;
        OldUnitPrice = item.UnitPrice;
        NewUnitPrice = newUnitPrice;
        var oldAmount = item.Quantity * item.UnitPrice;
        var newAmount = (newQuantity ?? item.Quantity) * (newUnitPrice ?? item.UnitPrice);
        ValueChange = decimal.Round(newAmount - oldAmount, 0, MidpointRounding.AwayFromZero);
    }

    public Guid Id { get; private set; }

    public Guid AppendixId { get; private set; }

    public Guid ContractItemId { get; private set; }

    public string ItemName { get; private set; }

    public decimal OldQuantity { get; private set; }

    public decimal? NewQuantity { get; private set; }

    public decimal OldUnitPrice { get; private set; }

    public decimal? NewUnitPrice { get; private set; }

    /// <summary>Change of the contract value (VAT included) caused by this line.</summary>
    public decimal ValueChange { get; private set; }
}
