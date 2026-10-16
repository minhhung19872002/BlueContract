using BlueContract.Domain.Common;

namespace BlueContract.Domain.Bids;

public enum BidDecisionStatus
{
    /// <summary>Đang nhập liệu: danh mục còn được đồng bộ / import.</summary>
    Draft = 0,

    /// <summary>Đã ban hành: dùng để tạo hợp đồng.</summary>
    Active = 1,

    /// <summary>Hết hiệu lực / đã thực hiện xong.</summary>
    Closed = 2,
}

/// <summary>
/// Quyết định phê duyệt kết quả lựa chọn nhà thầu. Linked to the HIS by (decision number + supplier tax code) — spec §5.2.
/// </summary>
public sealed class BidDecision : Entity
{
    private readonly List<BidDecisionSupplier> _suppliers = [];
    private readonly List<BidDecisionItem> _items = [];

    private BidDecision()
    {
        Number = string.Empty;
        Summary = string.Empty;
        PackageName = string.Empty;
    }

    public BidDecision(string number, DateOnly signedDate, string summary, string packageName, Guid categoryId)
    {
        Number = NormalizeNumber(number);
        SignedDate = signedDate;
        Summary = Guard.Required(summary, "BID_SUMMARY_REQUIRED");
        PackageName = Guard.Required(packageName, "BID_PACKAGE_REQUIRED");
        CategoryId = categoryId;
        Status = BidDecisionStatus.Draft;
    }

    /// <summary>Số quyết định (unique within the hospital).</summary>
    public string Number { get; private set; }

    public DateOnly SignedDate { get; private set; }

    /// <summary>Trích yếu.</summary>
    public string Summary { get; private set; }

    public string PackageName { get; private set; }

    /// <summary>Hình thức lựa chọn nhà thầu (đấu thầu rộng rãi, chào hàng cạnh tranh...).</summary>
    public string? SelectionMethod { get; private set; }

    public string? FundingSource { get; private set; }

    /// <summary>Thời gian thực hiện hợp đồng (free text, e.g. "12 tháng").</summary>
    public string? ExecutionPeriod { get; private set; }

    /// <summary>Main category of the package (drives the import template and the default contract template).</summary>
    public Guid CategoryId { get; private set; }

    public Guid? FacilityId { get; private set; }

    public DateOnly? ValidUntil { get; private set; }

    public string? AttachmentKey { get; private set; }

    public string? AttachmentName { get; private set; }

    public string? AttachmentSha256 { get; private set; }

    public BidDecisionStatus Status { get; private set; }

    public IReadOnlyCollection<BidDecisionSupplier> Suppliers => _suppliers;

    public IReadOnlyCollection<BidDecisionItem> Items => _items;

    public static string NormalizeNumber(string? number) =>
        Guard.Required(number, "BID_NUMBER_REQUIRED").Replace("  ", " ", StringComparison.Ordinal);

    public void Update(
        string number,
        DateOnly signedDate,
        string summary,
        string packageName,
        string? selectionMethod,
        string? fundingSource,
        string? executionPeriod,
        Guid categoryId,
        Guid? facilityId,
        DateOnly? validUntil)
    {
        if (Status == BidDecisionStatus.Closed)
        {
            throw new DomainException("BID_CLOSED");
        }

        var normalized = NormalizeNumber(number);
        if (normalized != Number && Status != BidDecisionStatus.Draft)
        {
            // The number is the HIS link key: frozen once contracts may reference it.
            throw new DomainException("BID_NUMBER_LOCKED");
        }

        Number = normalized;
        SignedDate = signedDate;
        Summary = Guard.Required(summary, "BID_SUMMARY_REQUIRED");
        PackageName = Guard.Required(packageName, "BID_PACKAGE_REQUIRED");
        SelectionMethod = Guard.Optional(selectionMethod);
        FundingSource = Guard.Optional(fundingSource);
        ExecutionPeriod = Guard.Optional(executionPeriod);
        CategoryId = categoryId;
        FacilityId = facilityId;
        ValidUntil = validUntil;
    }

    public void AttachFile(string key, string fileName, string sha256)
    {
        AttachmentKey = key;
        AttachmentName = Guard.Required(fileName, "BID_ATTACHMENT_REQUIRED");
        AttachmentSha256 = sha256;
    }

    /// <summary>Adds or updates a winning supplier with its winning value.</summary>
    public BidDecisionSupplier UpsertSupplier(Guid supplierId, decimal winningValue)
    {
        EnsureOpen();
        if (winningValue < 0)
        {
            throw new DomainException("BID_VALUE_INVALID");
        }

        var row = _suppliers.FirstOrDefault(s => s.SupplierId == supplierId);
        if (row is null)
        {
            row = new BidDecisionSupplier(Id, supplierId, winningValue);
            _suppliers.Add(row);
        }
        else
        {
            row.WinningValue = winningValue;
        }

        return row;
    }

    public void RemoveSupplier(Guid supplierId)
    {
        EnsureOpen();
        if (_items.Any(i => i.SupplierId == supplierId))
        {
            throw new DomainException("BID_SUPPLIER_HAS_ITEMS");
        }

        _suppliers.RemoveAll(s => s.SupplierId == supplierId);
    }

    /// <summary>Adds or updates a winning line (from HIS sync, Excel import or manual entry).</summary>
    public BidDecisionItem UpsertItem(Guid itemId, Guid supplierId, decimal quantity, decimal unitPrice)
    {
        EnsureOpen();
        if (_suppliers.All(s => s.SupplierId != supplierId))
        {
            throw new DomainException("BID_SUPPLIER_NOT_WINNER");
        }

        if (quantity <= 0)
        {
            throw new DomainException("BID_QUANTITY_INVALID");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("BID_PRICE_INVALID");
        }

        var row = _items.FirstOrDefault(i => i.ItemId == itemId);
        if (row is null)
        {
            row = new BidDecisionItem(Id, itemId, supplierId, quantity, unitPrice);
            _items.Add(row);
        }
        else
        {
            if (row.SupplierId != supplierId)
            {
                throw new DomainException("BID_ITEM_SUPPLIER_MISMATCH");
            }

            row.Quantity = quantity;
            row.UnitPrice = unitPrice;
        }

        return row;
    }

    public void RemoveItem(Guid itemId)
    {
        EnsureOpen();
        _items.RemoveAll(i => i.ItemId == itemId);
    }

    /// <summary>Ban hành: contracts can now be created from the decision.</summary>
    public void Activate()
    {
        if (Status != BidDecisionStatus.Draft)
        {
            throw new DomainException("BID_NOT_DRAFT");
        }

        if (_suppliers.Count == 0)
        {
            throw new DomainException("BID_NO_SUPPLIER");
        }

        if (_items.Count == 0)
        {
            throw new DomainException("BID_NO_ITEMS");
        }

        Status = BidDecisionStatus.Active;
    }

    public void Close() => Status = BidDecisionStatus.Closed;

    private void EnsureOpen()
    {
        if (Status == BidDecisionStatus.Closed)
        {
            throw new DomainException("BID_CLOSED");
        }
    }
}

/// <summary>NCC trúng thầu trong quyết định.</summary>
public sealed class BidDecisionSupplier
{
    private BidDecisionSupplier()
    {
    }

    internal BidDecisionSupplier(Guid bidDecisionId, Guid supplierId, decimal winningValue)
    {
        Id = Guid.CreateVersion7();
        BidDecisionId = bidDecisionId;
        SupplierId = supplierId;
        WinningValue = winningValue;
    }

    public Guid Id { get; private set; }

    public Guid BidDecisionId { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>Giá trúng thầu của NCC.</summary>
    public decimal WinningValue { get; internal set; }
}

/// <summary>Hàng hóa trúng thầu: winning quantity and unit price per supplier.</summary>
public sealed class BidDecisionItem
{
    private BidDecisionItem()
    {
    }

    internal BidDecisionItem(Guid bidDecisionId, Guid itemId, Guid supplierId, decimal quantity, decimal unitPrice)
    {
        Id = Guid.CreateVersion7();
        BidDecisionId = bidDecisionId;
        ItemId = itemId;
        SupplierId = supplierId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }

    public Guid BidDecisionId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid SupplierId { get; private set; }

    public decimal Quantity { get; internal set; }

    public decimal UnitPrice { get; internal set; }

    public decimal Amount => decimal.Round(Quantity * UnitPrice, 0, MidpointRounding.AwayFromZero);
}
