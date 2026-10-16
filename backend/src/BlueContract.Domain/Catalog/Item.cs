using BlueContract.Domain.Common;
using BlueContract.Domain.Identity;

namespace BlueContract.Domain.Catalog;

/// <summary>Which Excel import template a category uses (spec §10 items 1–2).</summary>
public enum ItemImportKind
{
    /// <summary>Thuốc, vật tư y tế, hóa chất, sinh phẩm xét nghiệm.</summary>
    Medical = 0,

    /// <summary>Hàng hóa, tài sản, dịch vụ, sửa chữa.</summary>
    General = 1,
}

/// <summary>Hạng mục hàng hóa: Thuốc, VTYT, Hóa chất, Sinh phẩm XN, Tài sản/TTB, Hàng hóa, Dịch vụ, Sửa chữa.</summary>
public sealed class ItemCategory : Entity
{
    private ItemCategory()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public ItemCategory(string code, string name, ProcurementDomain domain, ItemImportKind importKind, string? defaultContractTemplateCode, int sortOrder)
    {
        Code = Guard.Required(code, "ITEM_CATEGORY_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "ITEM_CATEGORY_NAME_REQUIRED");
        Domain = domain;
        ImportKind = importKind;
        DefaultContractTemplateCode = Guard.Optional(defaultContractTemplateCode);
        SortOrder = sortOrder;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Mảng phụ trách (drives data scope of aggregators / domain heads).</summary>
    public ProcurementDomain Domain { get; private set; }

    public ItemImportKind ImportKind { get; private set; }

    /// <summary>Contract template suggested by the wizard for this category.</summary>
    public string? DefaultContractTemplateCode { get; private set; }

    public int SortOrder { get; private set; }
}

/// <summary>Where an item record came from (spec §9.1-4 "xem nguồn").</summary>
public enum ItemSource
{
    His = 0,
    Import = 1,
    Manual = 2,
}

/// <summary>
/// Hàng hóa / dịch vụ. Items of a bid decision belong to one winning supplier; <see cref="HisKey"/> links the row to
/// the hospital information system (spec §5.1).
/// </summary>
public sealed class Item : Entity
{
    private Item()
    {
        Code = string.Empty;
        Name = string.Empty;
        Unit = string.Empty;
    }

    public Item(string code, string name, string unit, Guid categoryId, ItemSource source)
    {
        Code = Guard.Required(code, "ITEM_CODE_REQUIRED");
        Name = Guard.Required(name, "ITEM_NAME_REQUIRED");
        Unit = Guard.Required(unit, "ITEM_UNIT_REQUIRED");
        CategoryId = categoryId;
        Source = source;
        IsActive = true;
    }

    /// <summary>Mã key HIS (null for items managed only on the portal, e.g. services without stock).</summary>
    public string? HisKey { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Hoạt chất (thuốc) hoặc thông số kỹ thuật (hàng hóa).</summary>
    public string? ActiveIngredient { get; private set; }

    /// <summary>Nồng độ, hàm lượng.</summary>
    public string? Strength { get; private set; }

    /// <summary>Quy cách đóng gói.</summary>
    public string? Specification { get; private set; }

    public string Unit { get; private set; }

    public string? Manufacturer { get; private set; }

    public string? Country { get; private set; }

    /// <summary>Số đăng ký / số giấy phép nhập khẩu.</summary>
    public string? RegistrationNumber { get; private set; }

    /// <summary>Nhóm thầu (VD: N1, N2... với thuốc generic).</summary>
    public string? BidGroup { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid? SupplierId { get; private set; }

    public Guid? BidDecisionId { get; private set; }

    /// <summary>Đơn giá trúng thầu: contract unit prices may not exceed it.</summary>
    public decimal? WinningUnitPrice { get; private set; }

    public Guid? FacilityId { get; private set; }

    public ItemSource Source { get; private set; }

    public bool IsActive { get; private set; }

    public void UpdateDetails(
        string name,
        string unit,
        string? activeIngredient,
        string? strength,
        string? specification,
        string? manufacturer,
        string? country,
        string? registrationNumber,
        string? bidGroup,
        bool isActive)
    {
        Name = Guard.Required(name, "ITEM_NAME_REQUIRED");
        Unit = Guard.Required(unit, "ITEM_UNIT_REQUIRED");
        ActiveIngredient = Guard.Optional(activeIngredient);
        Strength = Guard.Optional(strength);
        Specification = Guard.Optional(specification);
        Manufacturer = Guard.Optional(manufacturer);
        Country = Guard.Optional(country);
        RegistrationNumber = Guard.Optional(registrationNumber);
        BidGroup = Guard.Optional(bidGroup);
        IsActive = isActive;
    }

    public void SetHisKey(string? hisKey) => HisKey = Guard.Optional(hisKey);

    public void SetCategory(Guid categoryId) => CategoryId = categoryId;

    public void SetFacility(Guid? facilityId) => FacilityId = facilityId;

    /// <summary>Links the item to the winning supplier of a bid decision at the winning unit price.</summary>
    public void AssignToBid(Guid bidDecisionId, Guid supplierId, decimal winningUnitPrice)
    {
        if (winningUnitPrice < 0)
        {
            throw new DomainException("ITEM_PRICE_INVALID");
        }

        BidDecisionId = bidDecisionId;
        SupplierId = supplierId;
        WinningUnitPrice = winningUnitPrice;
    }

    /// <summary>Items bought outside any bid (ngoài thầu) still belong to the contract's supplier.</summary>
    public void AssignToSupplier(Guid supplierId) => SupplierId = supplierId;

    /// <summary>The HIS overwrites descriptive data when re-synchronised; the source becomes HIS.</summary>
    public void MarkSyncedFromHis(string hisKey)
    {
        HisKey = Guard.Required(hisKey, "ITEM_HIS_KEY_REQUIRED");
        Source = ItemSource.His;
    }
}
