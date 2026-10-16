using BlueContract.Domain.Common;

namespace BlueContract.Domain.Identity;

public sealed class Role : Entity
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Role(string code, string name, string? description, bool isSystem)
    {
        Code = Guard.Required(code, "ROLE_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "ROLE_NAME_REQUIRED");
        Description = Guard.Optional(description);
        IsSystem = isSystem;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Seeded role; cannot be deleted (permissions remain editable).</summary>
    public bool IsSystem { get; private set; }

    /// <summary>Supplier roles can only be granted to supplier users.</summary>
    public bool IsSupplierRole => Code.StartsWith("SUPPLIER_", StringComparison.Ordinal);

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public void Update(string name, string? description)
    {
        Name = Guard.Required(name, "ROLE_NAME_REQUIRED");
        Description = Guard.Optional(description);
    }

    public void SetPermissions(IEnumerable<string> permissionCodes)
    {
        var wanted = permissionCodes.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var unknown = wanted.Where(p => !PermissionCatalog.Exists(p)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException("PERMISSION_UNKNOWN", string.Join(", ", unknown));
        }

        _permissions.RemoveAll(p => !wanted.Contains(p.PermissionCode));
        foreach (var code in wanted.Where(c => _permissions.All(p => p.PermissionCode != c)))
        {
            _permissions.Add(new RolePermission(Id, code));
        }
    }
}

public sealed class RolePermission
{
    private RolePermission()
    {
        PermissionCode = string.Empty;
    }

    public RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; }
}

public sealed class UserRole
{
    private UserRole()
    {
    }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Role? Role { get; private set; }
}

public sealed class UserDataScope
{
    private UserDataScope()
    {
        Value = string.Empty;
    }

    public UserDataScope(Guid userId, DataScopeType scopeType, string value)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        ScopeType = scopeType;
        Value = Guard.Required(value, "DATA_SCOPE_VALUE_REQUIRED");
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public DataScopeType ScopeType { get; private set; }

    /// <summary>Facility/Department id (Guid string) or ProcurementDomain name.</summary>
    public string Value { get; private set; }
}
