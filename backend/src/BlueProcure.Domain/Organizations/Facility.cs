using BlueProcure.Domain.Common;

namespace BlueProcure.Domain.Organizations;

/// <summary>Cơ sở — a physical campus of the hospital.</summary>
public sealed class Facility : Entity
{
    private Facility()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Facility(string code, string name, string? address)
    {
        Code = Guard.Required(code, "FACILITY_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "FACILITY_NAME_REQUIRED");
        Address = Guard.Optional(address);
        IsActive = true;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Address { get; private set; }

    public string? HisFacilityCode { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? address, string? hisFacilityCode, bool isActive)
    {
        Name = Guard.Required(name, "FACILITY_NAME_REQUIRED");
        Address = Guard.Optional(address);
        HisFacilityCode = Guard.Optional(hisFacilityCode);
        IsActive = isActive;
    }
}
