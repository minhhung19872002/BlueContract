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

/// <summary>Contract fields a "thay đổi thông tin" appendix may change (spec §5 "nội dung thay đổi có cấu trúc").</summary>
public enum ContractInfoField
{
    /// <summary>Tên hợp đồng.</summary>
    Name = 0,

    /// <summary>Thời gian thực hiện.</summary>
    Duration = 1,

    /// <summary>Điều khoản thanh toán.</summary>
    PaymentTerms = 2,

    /// <summary>Điều khoản giao hàng.</summary>
    DeliveryTerms = 3,

    /// <summary>Điều khoản bảo hành.</summary>
    WarrantyTerms = 4,

    /// <summary>Số bảo lãnh thực hiện hợp đồng.</summary>
    GuaranteeNumber = 5,

    /// <summary>Ngân hàng bảo lãnh.</summary>
    GuaranteeBank = 6,

    /// <summary>Giá trị bảo lãnh.</summary>
    GuaranteeAmount = 7,

    /// <summary>Hạn bảo lãnh.</summary>
    GuaranteeExpiry = 8,
}

/// <summary>Requested new value of a contract field (amount: invariant number, date: yyyy-MM-dd, empty = remove).</summary>
public sealed record AppendixInfoDraft(ContractInfoField Field, string? NewValue);

/// <summary>One changed field, old → new, as text in the invariant formats of <see cref="AppendixInfoDraft"/>.</summary>
public sealed record AppendixInfoChange(ContractInfoField Field, string? OldValue, string? NewValue);

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

    /// <summary>Structured field changes of a "thay đổi thông tin" appendix, applied to the contract when effective.</summary>
    public IReadOnlyList<AppendixInfoChange> InfoFields { get; private set; } = [];

    public DateOnly? OldExpiryDate { get; private set; }

    public DateOnly? NewExpiryDate { get; private set; }

    public Guid? DocumentId { get; private set; }

    public AppendixStatus Status { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

    public decimal ValueChange { get; private set; }

    public IReadOnlyCollection<ContractAppendixLine> Lines => _lines;

    public void SetContent(
        Contract contract,
        string reason,
        string? infoChanges,
        DateOnly? newExpiryDate,
        IReadOnlyCollection<AppendixLineDraft> lines,
        IReadOnlyCollection<AppendixInfoDraft>? infoFields = null)
    {
        if (Status != AppendixStatus.Draft)
        {
            throw new DomainException("APPENDIX_LOCKED");
        }

        Reason = Guard.Required(reason, "APPENDIX_REASON_REQUIRED");
        InfoChanges = Guard.Optional(infoChanges);
        OldExpiryDate = contract.ExpiryDate;
        NewExpiryDate = newExpiryDate;
        InfoFields = ReadInfoFields(contract, infoFields ?? []);

        switch (Type)
        {
            case AppendixType.Extension when newExpiryDate is null || (contract.ExpiryDate is { } old && newExpiryDate <= old):
                throw new DomainException("APPENDIX_EXPIRY_INVALID");
            case AppendixType.InfoChange when InfoChanges is null && InfoFields.Count == 0:
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

    private List<AppendixInfoChange> ReadInfoFields(Contract contract, IReadOnlyCollection<AppendixInfoDraft> drafts)
    {
        if (drafts.Count == 0)
        {
            return [];
        }

        if (Type != AppendixType.InfoChange)
        {
            throw new DomainException("APPENDIX_INFO_NOT_ALLOWED");
        }

        if (drafts.Select(d => d.Field).Distinct().Count() != drafts.Count)
        {
            throw new DomainException("APPENDIX_INFO_DUPLICATE");
        }

        var changes = new List<AppendixInfoChange>();
        foreach (var draft in drafts)
        {
            var newValue = ContractInfo.Normalize(draft.Field, draft.NewValue);
            var oldValue = ContractInfo.Read(contract, draft.Field);
            if (newValue == oldValue)
            {
                throw new DomainException("APPENDIX_INFO_UNCHANGED");
            }

            changes.Add(new AppendixInfoChange(draft.Field, oldValue, newValue));
        }

        return changes;
    }

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

/// <summary>Reads / validates / writes the contract fields of <see cref="ContractInfoField"/> as invariant text.</summary>
internal static class ContractInfo
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string? Read(Contract c, ContractInfoField field) => field switch
    {
        ContractInfoField.Name => c.Name,
        ContractInfoField.Duration => c.Duration,
        ContractInfoField.PaymentTerms => c.PaymentTerms,
        ContractInfoField.DeliveryTerms => c.DeliveryTerms,
        ContractInfoField.WarrantyTerms => c.WarrantyTerms,
        ContractInfoField.GuaranteeNumber => c.GuaranteeNumber,
        ContractInfoField.GuaranteeBank => c.GuaranteeBank,
        ContractInfoField.GuaranteeAmount => c.GuaranteeAmount?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        ContractInfoField.GuaranteeExpiry => c.GuaranteeExpiry?.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new DomainException("APPENDIX_INFO_FIELD_INVALID"),
    };

    /// <summary>Trims the value and checks its format; empty means "remove" (not allowed for the name).</summary>
    public static string? Normalize(ContractInfoField field, string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        switch (field)
        {
            case ContractInfoField.Name when text is null:
                throw new DomainException("APPENDIX_INFO_VALUE_INVALID");
            case ContractInfoField.GuaranteeAmount when text is not null:
                return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount >= 0
                    ? amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    : throw new DomainException("APPENDIX_INFO_VALUE_INVALID");
            case ContractInfoField.GuaranteeExpiry when text is not null:
                return DateOnly.TryParseExact(text, DateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
                    ? date.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture)
                    : throw new DomainException("APPENDIX_INFO_VALUE_INVALID");
            case < ContractInfoField.Name or > ContractInfoField.GuaranteeExpiry:
                throw new DomainException("APPENDIX_INFO_FIELD_INVALID");
            default:
                return text;
        }
    }

    public static decimal? Amount(string? value) =>
        value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    public static DateOnly? Date(string? value) =>
        value is null ? null : DateOnly.ParseExact(value, DateFormat, System.Globalization.CultureInfo.InvariantCulture);
}
