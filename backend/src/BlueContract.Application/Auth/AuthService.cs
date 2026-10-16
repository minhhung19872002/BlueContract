using BlueContract.Application.Common;
using BlueContract.Domain.Common;
using BlueContract.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlueContract.Application.Auth;

public sealed class AuthService(
    IAppDbContext db,
    IPasswordService passwords,
    ITokenService tokens,
    ITotpService totp,
    ISecretProtector protector,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<Result<LoginResult>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters().Include(u => u.DataScopes)
            .FirstOrDefaultAsync(u => u.UserName == userName && !u.IsDeleted, ct);

        if (user is null)
        {
            await audit.LogNowAsync(AuditActions.LoginFailed, nameof(User), null, new { userName }, "Tài khoản không tồn tại", ct);
            return Error.Unauthorized("INVALID_CREDENTIALS");
        }

        var now = clock.UtcNow;
        if (user.IsLockedOut(now))
        {
            return Error.Unauthorized("ACCOUNT_LOCKED");
        }

        var (valid, needsRehash) = passwords.Verify(user, request.Password);
        if (!valid)
        {
            return await FailLoginAsync(user, "Sai mật khẩu", ct);
        }

        if (!user.IsActive)
        {
            return Error.Unauthorized("ACCOUNT_INACTIVE");
        }

        if (needsRehash)
        {
            user.SetPasswordHash(passwords.Hash(user, request.Password), user.MustChangePassword);
        }

        if (user.TwoFactorEnabled)
        {
            await db.SaveChangesAsync(ct);
            return new LoginResult { RequiresTwoFactor = true, TwoFactorChallenge = tokens.CreateTwoFactorChallenge(user) };
        }

        return await CompleteLoginAsync(user, ct);
    }

    public async Task<Result<LoginResult>> LoginWithTwoFactorAsync(TwoFactorLoginRequest request, CancellationToken ct)
    {
        var userId = tokens.ValidateTwoFactorChallenge(request.Challenge, out var stamp);
        if (userId is null)
        {
            return Error.Unauthorized("TWO_FACTOR_CHALLENGE_EXPIRED");
        }

        var user = await db.Users.IgnoreQueryFilters().Include(u => u.DataScopes).FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null || user.SecurityStamp != stamp || !user.IsActive || !user.TwoFactorEnabled)
        {
            return Error.Unauthorized("TWO_FACTOR_CHALLENGE_EXPIRED");
        }

        if (user.IsLockedOut(clock.UtcNow))
        {
            return Error.Unauthorized("ACCOUNT_LOCKED");
        }

        if (!totp.Verify(protector.Unprotect(user.TwoFactorSecret!), request.Code, clock.UtcNow))
        {
            var failed = await FailLoginAsync(user, "Sai mã xác thực 2 lớp", ct);
            return failed.Error!.Code == "ACCOUNT_LOCKED" ? failed : Error.Unauthorized("TWO_FACTOR_INVALID");
        }

        return await CompleteLoginAsync(user, ct);
    }

    public async Task<Result<LoginResult>> RefreshAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return Error.Unauthorized("SESSION_EXPIRED");
        }

        var now = clock.UtcNow;
        var hash = tokens.HashRefreshToken(rawToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
        {
            return Error.Unauthorized("SESSION_EXPIRED");
        }

        if (stored.RevokedAt is not null)
        {
            // A rotated token was presented again: likely theft. Kill the whole session family.
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            audit.Log(AuditActions.RefreshTokenReuse, nameof(User), stored.UserId.ToString(), new { stored.FamilyId });
            await db.SaveChangesAsync(ct);
            return Error.Unauthorized("SESSION_EXPIRED");
        }

        if (!stored.IsActive(now))
        {
            return Error.Unauthorized("SESSION_EXPIRED");
        }

        var user = await db.Users.IgnoreQueryFilters().Include(u => u.DataScopes).FirstOrDefaultAsync(u => u.Id == stored.UserId && !u.IsDeleted, ct);
        if (user is null || !user.IsActive || user.IsLockedOut(now))
        {
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            await db.SaveChangesAsync(ct);
            return Error.Unauthorized("SESSION_EXPIRED");
        }

        var (newToken, newHash) = tokens.CreateRefreshToken();
        stored.Revoke(now, newHash);
        var expires = now.AddDays(_options.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken(user.Id, newHash, stored.FamilyId, now, expires, currentUser.IpAddress));

        var permissions = await LoadPermissionsAsync(user.Id, ct);
        var access = tokens.CreateAccessToken(user, permissions);
        await db.SaveChangesAsync(ct);

        return new LoginResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            User = await GetCurrentUserAsync(user.Id, ct),
            RefreshToken = newToken,
            RefreshTokenExpiresAt = expires,
        };
    }

    public async Task LogoutAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        var hash = tokens.HashRefreshToken(rawToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
        {
            return;
        }

        await RevokeFamilyAsync(stored.FamilyId, clock.UtcNow, ct);
        audit.Log(AuditActions.Logout, nameof(User), stored.UserId.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await RequireCurrentUserAsync(ct);
        if (user is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        if (!passwords.Verify(user, request.CurrentPassword).Valid)
        {
            return Result.Failure(new Error("CURRENT_PASSWORD_INVALID"));
        }

        user.SetPasswordHash(passwords.Hash(user, request.NewPassword), mustChange: false);
        await RevokeAllForUserAsync(user.Id, ct);
        audit.Log(AuditActions.PasswordChanged, nameof(User), user.Id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<TwoFactorSetupResponse>> BeginTwoFactorSetupAsync(string issuer, CancellationToken ct)
    {
        var user = await RequireCurrentUserAsync(ct);
        if (user is null)
        {
            return Error.Unauthorized();
        }

        if (user.TwoFactorEnabled)
        {
            return new Error("TWO_FACTOR_ALREADY_ENABLED");
        }

        var secret = totp.GenerateSecret();
        user.SetPendingTwoFactorSecret(protector.Protect(secret));
        await db.SaveChangesAsync(ct);
        return new TwoFactorSetupResponse(secret, totp.BuildOtpAuthUri(secret, user.UserName, issuer));
    }

    public async Task<Result> EnableTwoFactorAsync(EnableTwoFactorRequest request, CancellationToken ct)
    {
        var user = await RequireCurrentUserAsync(ct);
        if (user is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        if (user.TwoFactorSecret is null || !totp.Verify(protector.Unprotect(user.TwoFactorSecret), request.Code, clock.UtcNow))
        {
            return Result.Failure(new Error("TWO_FACTOR_INVALID"));
        }

        user.EnableTwoFactor();
        audit.Log(AuditActions.TwoFactorEnabled, nameof(User), user.Id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DisableTwoFactorAsync(DisableTwoFactorRequest request, CancellationToken ct)
    {
        var user = await RequireCurrentUserAsync(ct);
        if (user is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        if (!passwords.Verify(user, request.Password).Valid)
        {
            return Result.Failure(new Error("CURRENT_PASSWORD_INVALID"));
        }

        user.DisableTwoFactor();
        audit.Log(AuditActions.TwoFactorDisabled, nameof(User), user.Id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FullName,
                u.Email,
                u.JobTitle,
                u.UserType,
                u.SupplierId,
                SupplierName = db.Suppliers.IgnoreQueryFilters().Where(s => s.Id == u.SupplierId).Select(s => s.Name).FirstOrDefault(),
                u.DepartmentId,
                DepartmentName = db.Departments.Where(d => d.Id == u.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                u.FacilityId,
                FacilityName = db.Facilities.Where(f => f.Id == u.FacilityId).Select(f => f.Name).FirstOrDefault(),
                u.TwoFactorEnabled,
                u.MustChangePassword,
            })
            .FirstOrDefaultAsync(ct);
        if (user is null)
        {
            return null;
        }

        var roles = await db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => new { r.Code, r.Name })
            .OrderBy(r => r.Code)
            .ToListAsync(ct);
        var permissions = await LoadPermissionsAsync(userId, ct);

        return new CurrentUserDto(
            user.Id,
            user.UserName,
            user.FullName,
            user.Email,
            user.JobTitle,
            user.UserType,
            user.SupplierId,
            user.SupplierName,
            user.DepartmentId,
            user.DepartmentName,
            user.FacilityId,
            user.FacilityName,
            roles.Select(r => r.Code).ToList(),
            roles.Select(r => r.Name).ToList(),
            [.. permissions.Order(StringComparer.Ordinal)],
            user.TwoFactorEnabled,
            user.MustChangePassword);
    }

    public async Task<IReadOnlyCollection<string>> LoadPermissionsAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionCode)
            .Distinct()
            .ToListAsync(ct);

    private async Task<Result<LoginResult>> CompleteLoginAsync(User user, CancellationToken ct)
    {
        var now = clock.UtcNow;
        user.RegisterSuccessfulLogin(now);

        var permissions = await LoadPermissionsAsync(user.Id, ct);
        var access = tokens.CreateAccessToken(user, permissions);
        var (refreshToken, refreshHash) = tokens.CreateRefreshToken();
        var expires = now.AddDays(_options.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken(user.Id, refreshHash, Guid.CreateVersion7(), now, expires, currentUser.IpAddress));

        audit.Log(AuditActions.Login, nameof(User), user.Id.ToString(), new { user.UserName });
        await db.SaveChangesAsync(ct);

        return new LoginResult
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            User = await GetCurrentUserAsync(user.Id, ct),
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = expires,
        };
    }

    private async Task<Result<LoginResult>> FailLoginAsync(User user, string reason, CancellationToken ct)
    {
        var justLocked = user.RegisterFailedLogin(
            clock.UtcNow,
            _options.MaxFailedLogins,
            TimeSpan.FromMinutes(_options.LockoutMinutes));
        if (justLocked)
        {
            user.RotateSecurityStamp();
            await RevokeAllForUserAsync(user.Id, ct);
        }

        audit.Log(justLocked ? AuditActions.LockedOut : AuditActions.LoginFailed, nameof(User), user.Id.ToString(), new { user.UserName }, reason);
        await db.SaveChangesAsync(ct);
        return Error.Unauthorized(justLocked ? "ACCOUNT_LOCKED" : "INVALID_CREDENTIALS");
    }

    private async Task<User?> RequireCurrentUserAsync(CancellationToken ct) =>
        currentUser.UserId is { } id
            ? await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct)
            : null;

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.Revoke(now));
    }

    private async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.Revoke(now));
    }
}
