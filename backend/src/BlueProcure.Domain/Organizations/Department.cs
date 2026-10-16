using BlueProcure.Domain.Common;

namespace BlueProcure.Domain.Organizations;

public enum DepartmentType
{
    /// <summary>Lâm sàng.</summary>
    Clinical = 0,

    /// <summary>Cận lâm sàng.</summary>
    Paraclinical = 1,

    /// <summary>Phòng chức năng.</summary>
    Functional = 2,
}

/// <summary>Khoa/Phòng.</summary>
public sealed class Department : Entity, IHasHistory
{
    private Department()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Department(string code, string name, DepartmentType type, Guid facilityId, string? hisDepartmentCode)
    {
        Code = Guard.Required(code, "DEPARTMENT_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "DEPARTMENT_NAME_REQUIRED");
        Type = type;
        FacilityId = facilityId;
        HisDepartmentCode = Guard.Optional(hisDepartmentCode);
        IsActive = true;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public DepartmentType Type { get; private set; }

    public Guid FacilityId { get; private set; }

    public Facility? Facility { get; private set; }

    public string? HisDepartmentCode { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, DepartmentType type, Guid facilityId, string? hisDepartmentCode, bool isActive)
    {
        Name = Guard.Required(name, "DEPARTMENT_NAME_REQUIRED");
        Type = type;
        FacilityId = facilityId;
        HisDepartmentCode = Guard.Optional(hisDepartmentCode);
        IsActive = isActive;
    }
}
