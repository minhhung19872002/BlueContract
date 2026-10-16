using BlueProcure.Domain.Common;

namespace BlueProcure.Domain.Organizations;

public enum WarehouseType
{
    Drug = 0,
    MedicalSupply = 1,
    Chemical = 2,
    Asset = 3,
    General = 4,
}

public sealed class Warehouse : Entity
{
    private Warehouse()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Warehouse(string code, string name, WarehouseType type, Guid facilityId, string? hisWarehouseCode)
    {
        Code = Guard.Required(code, "WAREHOUSE_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "WAREHOUSE_NAME_REQUIRED");
        Type = type;
        FacilityId = facilityId;
        HisWarehouseCode = Guard.Optional(hisWarehouseCode);
        IsActive = true;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public WarehouseType Type { get; private set; }

    public Guid FacilityId { get; private set; }

    public Facility? Facility { get; private set; }

    public string? HisWarehouseCode { get; private set; }

    /// <summary>Department head who signs receipts for this warehouse (Trưởng khoa Dược/Vật tư...).</summary>
    public Guid? ManagingDepartmentId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, WarehouseType type, Guid facilityId, string? hisWarehouseCode, Guid? managingDepartmentId, bool isActive)
    {
        Name = Guard.Required(name, "WAREHOUSE_NAME_REQUIRED");
        Type = type;
        FacilityId = facilityId;
        HisWarehouseCode = Guard.Optional(hisWarehouseCode);
        ManagingDepartmentId = managingDepartmentId;
        IsActive = isActive;
    }
}
