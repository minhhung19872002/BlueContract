namespace BlueContract.Domain.Identity;

public enum UserType
{
    Internal = 0,
    Supplier = 1,
}

public enum UserStatus
{
    Active = 0,
    Inactive = 1,

    /// <summary>Created for a supplier, waiting for the activation link to be used.</summary>
    PendingActivation = 2,
}

public enum DataScopeType
{
    Facility = 0,
    Department = 1,
    Domain = 2,
}

/// <summary>Mảng phụ trách — groups items/contracts for scoping and approval routing.</summary>
public enum ProcurementDomain
{
    /// <summary>Thuốc.</summary>
    Drug = 0,

    /// <summary>Vật tư y tế.</summary>
    MedicalSupply = 1,

    /// <summary>Hóa chất, sinh phẩm xét nghiệm.</summary>
    Chemical = 2,

    /// <summary>Tài sản, trang thiết bị (VTKT-TTB-CNTT).</summary>
    Equipment = 3,

    /// <summary>Hành chính: hàng hóa khác, dịch vụ, sửa chữa (TCHC).</summary>
    Administration = 4,
}
