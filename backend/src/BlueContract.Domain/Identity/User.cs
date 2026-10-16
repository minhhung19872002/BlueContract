using BlueContract.Domain.Common;

namespace BlueContract.Domain.Identity;

public sealed class User : Entity, IHasHistory
{
    private readonly List<UserRole> _roles = [];
    private readonly List<UserDataScope> _dataScopes = [];

    private User()
    {
        UserName = string.Empty;
        FullName = string.Empty;
        PasswordHash = string.Empty;
        SecurityStamp = string.Empty;
    }

    public User(string userName, string fullName, string? email, UserType userType, Guid? supplierId)
    {
        UserName = NormalizeUserName(userName);
        FullName = Guard.Required(fullName, "USER_FULL_NAME_REQUIRED");
        Email = Guard.Optional(email);
        UserType = userType;
        if (userType == UserType.Supplier && supplierId is null)
        {
            throw new DomainException("USER_SUPPLIER_REQUIRED");
        }

        SupplierId = userType == UserType.Supplier ? supplierId : null;
        Status = UserStatus.Active;
        PasswordHash = string.Empty;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public string UserName { get; private set; }

    public string FullName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Chức danh, e.g. "Phó Giám đốc", "Kế toán trưởng".</summary>
    public string? JobTitle { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public Guid? FacilityId { get; private set; }

    public UserType UserType { get; private set; }

    public Guid? SupplierId { get; private set; }

    public UserStatus Status { get; private set; }

    public string PasswordHash { get; private set; }

    /// <summary>Changes whenever credentials change; tokens issued before are invalid.</summary>
    public string SecurityStamp { get; private set; }

    public bool MustChangePassword { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>Base32 TOTP secret (stored protected by the infrastructure layer).</summary>
    public string? TwoFactorSecret { get; private set; }

    // Registered digital certificate (chứng thư số) info for signing.
    public string? CertificateSerial { get; private set; }

    public string? CertificateIssuer { get; private set; }

    public DateTimeOffset? CertificateValidTo { get; private set; }

    public string? SignatureImageFileKey { get; private set; }

    public string? InitialImageFileKey { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles;

    public IReadOnlyCollection<UserDataScope> DataScopes => _dataScopes;

    public bool IsActive => Status == UserStatus.Active;

    public void UpdateProfile(string fullName, string? email, string? phone, string? jobTitle, Guid? departmentId, Guid? facilityId)
    {
        FullName = Guard.Required(fullName, "USER_FULL_NAME_REQUIRED");
        Email = Guard.Optional(email);
        Phone = Guard.Optional(phone);
        JobTitle = Guard.Optional(jobTitle);
        DepartmentId = UserType == UserType.Internal ? departmentId : null;
        FacilityId = UserType == UserType.Internal ? facilityId : null;
    }

    public void SetStatus(UserStatus status)
    {
        Status = status;
        if (status != UserStatus.Active)
        {
            RotateSecurityStamp();
        }
    }

    public void SetPasswordHash(string passwordHash, bool mustChange)
    {
        PasswordHash = Guard.Required(passwordHash, "USER_PASSWORD_REQUIRED");
        MustChangePassword = mustChange;
        FailedLoginCount = 0;
        LockoutEnd = null;
        RotateSecurityStamp();
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is not null && LockoutEnd > now;

    /// <summary>Counts a failed login; locks the account once <paramref name="maxAttempts"/> is reached. Returns true when it just locked.</summary>
    public bool RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockoutEnd = now.Add(lockoutDuration);
            FailedLoginCount = 0;
            return true;
        }

        return false;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    public void Unlock()
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
    }

    public void SetPendingTwoFactorSecret(string secret) => TwoFactorSecret = Guard.Required(secret, "TWO_FACTOR_SECRET_REQUIRED");

    public void EnableTwoFactor()
    {
        if (string.IsNullOrEmpty(TwoFactorSecret))
        {
            throw new DomainException("TWO_FACTOR_NOT_SETUP");
        }

        TwoFactorEnabled = true;
        RotateSecurityStamp();
    }

    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
        RotateSecurityStamp();
    }

    public void RegisterCertificate(string? serial, string? issuer, DateTimeOffset? validTo)
    {
        CertificateSerial = Guard.Optional(serial);
        CertificateIssuer = Guard.Optional(issuer);
        CertificateValidTo = validTo;
    }

    public void SetSignatureImages(string? signatureImageKey, string? initialImageKey)
    {
        SignatureImageFileKey = signatureImageKey;
        InitialImageFileKey = initialImageKey;
    }

    public void SetRoles(IEnumerable<Guid> roleIds)
    {
        var wanted = roleIds.Distinct().ToHashSet();
        _roles.RemoveAll(r => !wanted.Contains(r.RoleId));
        foreach (var roleId in wanted.Where(id => _roles.All(r => r.RoleId != id)))
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }

    public void SetDataScopes(IEnumerable<(DataScopeType Type, string Value)> scopes)
    {
        var wanted = scopes.Select(s => (s.Type, Value: s.Value.Trim())).Distinct().ToList();
        _dataScopes.RemoveAll(s => !wanted.Contains((s.ScopeType, s.Value)));
        foreach (var (type, value) in wanted.Where(w => !_dataScopes.Any(s => s.ScopeType == w.Type && s.Value == w.Value)))
        {
            _dataScopes.Add(new UserDataScope(Id, type, value));
        }
    }

    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");

    public static string NormalizeUserName(string? userName)
    {
        var value = Guard.Required(userName, "USER_NAME_REQUIRED").ToLowerInvariant();
        if (value.Length is < 3 or > 64 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw new DomainException("USER_NAME_INVALID");
        }

        return value;
    }
}
