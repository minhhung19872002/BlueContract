using System.Linq.Expressions;
using System.Net;
using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using BlueContract.Domain.Common;
using BlueContract.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Admin;

public sealed record UserListItemDto(
    Guid Id,
    string UserName,
    string FullName,
    string? Email,
    string? JobTitle,
    UserType UserType,
    UserStatus Status,
    string? DepartmentName,
    string? FacilityName,
    string? SupplierName,
    IReadOnlyList<string> RoleCodes,
    bool IsLockedOut,
    bool TwoFactorEnabled,
    DateTimeOffset? LastLoginAt);

public sealed record DataScopeDto(DataScopeType Type, string Value);

public sealed record UserDetailDto(
    Guid Id,
    string UserName,
    string FullName,
    string? Email,
    string? Phone,
    string? JobTitle,
    UserType UserType,
    UserStatus Status,
    Guid? DepartmentId,
    Guid? FacilityId,
    Guid? SupplierId,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<DataScopeDto> DataScopes,
    string? CertificateSerial,
    string? CertificateIssuer,
    DateTimeOffset? CertificateValidTo,
    bool IsLockedOut,
    bool TwoFactorEnabled,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(
    string UserName,
    string FullName,
    string? Email,
    string? Phone,
    string? JobTitle,
    UserType UserType,
    Guid? DepartmentId,
    Guid? FacilityId,
    Guid? SupplierId,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<DataScopeDto>? DataScopes,
    string? Password);

public sealed record UpdateUserRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? JobTitle,
    Guid? DepartmentId,
    Guid? FacilityId,
    UserStatus Status,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<DataScopeDto>? DataScopes,
    string? CertificateSerial,
    string? CertificateIssuer,
    DateTimeOffset? CertificateValidTo);

public sealed record UserListFilter(UserType? UserType, Guid? SupplierId, Guid? DepartmentId, string? RoleCode, UserStatus? Status);

public sealed record ResetPasswordResponse(string TemporaryPassword, bool EmailSent);

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithName("Tên đăng nhập")
            .Matches("^[A-Za-z0-9._-]{3,64}$").WithMessage("Tên đăng nhập 3–64 ký tự, chỉ gồm chữ, số, dấu chấm, gạch dưới, gạch ngang.");
        RuleFor(x => x.FullName).NotEmpty().WithName("Họ tên").MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithName("Email");
        RuleFor(x => x.UserType).IsInEnum();
        RuleFor(x => x.SupplierId).NotEmpty().When(x => x.UserType == UserType.Supplier).WithName("Nhà cung cấp");
        RuleFor(x => x.RoleIds).NotEmpty().WithName("Vai trò");
        RuleFor(x => x.Password).Must(PasswordPolicy.IsStrong!).When(x => !string.IsNullOrEmpty(x.Password))
            .WithMessage("Mật khẩu phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường, chữ số và ký tự đặc biệt.");
    }
}

internal sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithName("Họ tên").MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithName("Email");
        RuleFor(x => x.Status).IsInEnum().WithName("Trạng thái");
        RuleFor(x => x.RoleIds).NotEmpty().WithName("Vai trò");
    }
}

public sealed class UserService(
    IAppDbContext db,
    IPasswordService passwords,
    IEmailSender email,
    IAppLinkBuilder links,
    IAuditLogger audit,
    IClock clock)
{
    private static readonly Dictionary<string, Expression<Func<User, object?>>> Sorts = new()
    {
        ["userName"] = x => x.UserName,
        ["fullName"] = x => x.FullName,
        ["lastLoginAt"] = x => x.LastLoginAt,
    };

    public Task<PagedResult<UserListItemDto>> ListAsync(PageQuery q, UserListFilter f, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(u => EF.Functions.ILike(u.UserName, p) || EF.Functions.ILike(u.FullName, p) || EF.Functions.ILike(u.Email!, p));
        }

        if (f.UserType is not null)
        {
            query = query.Where(u => u.UserType == f.UserType);
        }

        if (f.SupplierId is not null)
        {
            query = query.Where(u => u.SupplierId == f.SupplierId);
        }

        if (f.DepartmentId is not null)
        {
            query = query.Where(u => u.DepartmentId == f.DepartmentId);
        }

        if (f.Status is not null)
        {
            query = query.Where(u => u.Status == f.Status);
        }

        if (!string.IsNullOrWhiteSpace(f.RoleCode))
        {
            var code = f.RoleCode.ToUpperInvariant();
            query = query.Where(u => u.Roles.Any(r => r.Role!.Code == code));
        }

        return query.ApplySort(q, Sorts, "userName").Select(u => new UserListItemDto(
                u.Id,
                u.UserName,
                u.FullName,
                u.Email,
                u.JobTitle,
                u.UserType,
                u.Status,
                db.Departments.Where(d => d.Id == u.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                db.Facilities.Where(x => x.Id == u.FacilityId).Select(x => x.Name).FirstOrDefault(),
                db.Suppliers.Where(s => s.Id == u.SupplierId).Select(s => s.Name).FirstOrDefault(),
                u.Roles.Select(r => r.Role!.Code).ToList(),
                u.LockoutEnd != null && u.LockoutEnd > now,
                u.TwoFactorEnabled,
                u.LastLoginAt))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<UserDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).Include(u => u.DataScopes).FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? Error.NotFound() : ToDetail(user);
    }

    public async Task<Result<UserDetailDto>> CreateAsync(CreateUserRequest r, CancellationToken ct)
    {
        var userName = User.NormalizeUserName(r.UserName);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == userName, ct))
        {
            return Error.Conflict("USER_NAME_EXISTS");
        }

        if (r.UserType == UserType.Supplier && !await db.Suppliers.AnyAsync(s => s.Id == r.SupplierId, ct))
        {
            return Error.Validation("SUPPLIER_NOT_FOUND");
        }

        var roleCheck = await ValidateRolesAsync(r.RoleIds, r.UserType, ct);
        if (roleCheck is not null)
        {
            return roleCheck;
        }

        var user = new User(userName, r.FullName, r.Email, r.UserType, r.SupplierId);
        user.UpdateProfile(r.FullName, r.Email, r.Phone, r.JobTitle, r.DepartmentId, r.FacilityId);
        var generated = string.IsNullOrEmpty(r.Password);
        var password = generated ? PasswordPolicy.GenerateTemporary() : r.Password!;
        user.SetPasswordHash(passwords.Hash(user, password), mustChange: true);
        user.SetRoles(r.RoleIds);
        user.SetDataScopes((r.DataScopes ?? []).Select(s => (s.Type, s.Value)));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        if (generated)
        {
            await SendPasswordEmailAsync(user, password, isNew: true, ct);
        }

        return ToDetail(user);
    }

    public async Task<Result<UserDetailDto>> UpdateAsync(Guid id, UpdateUserRequest r, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).Include(u => u.DataScopes).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return Error.NotFound();
        }

        var roleCheck = await ValidateRolesAsync(r.RoleIds, user.UserType, ct);
        if (roleCheck is not null)
        {
            return roleCheck;
        }

        var rolesChanged = !user.Roles.Select(x => x.RoleId).ToHashSet().SetEquals(r.RoleIds);
        user.UpdateProfile(r.FullName, r.Email, r.Phone, r.JobTitle, r.DepartmentId, r.FacilityId);
        user.SetStatus(r.Status);
        user.SetRoles(r.RoleIds);
        user.SetDataScopes((r.DataScopes ?? []).Select(s => (s.Type, s.Value)));
        user.RegisterCertificate(r.CertificateSerial, r.CertificateIssuer, r.CertificateValidTo);
        if (rolesChanged)
        {
            // Force re-issue of access tokens so new permissions apply immediately.
            user.RotateSecurityStamp();
        }

        await db.SaveChangesAsync(ct);
        return ToDetail(user);
    }

    public async Task<Result> UnlockAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return Result.Failure(Error.NotFound());
        }

        user.Unlock();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ResetPasswordResponse>> ResetPasswordAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return Error.NotFound();
        }

        var password = PasswordPolicy.GenerateTemporary();
        user.SetPasswordHash(passwords.Hash(user, password), mustChange: true);
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
        tokens.ForEach(t => t.Revoke(clock.UtcNow));
        audit.Log(AuditActions.PasswordReset, nameof(User), id.ToString());
        await db.SaveChangesAsync(ct);

        var sent = await SendPasswordEmailAsync(user, password, isNew: false, ct);
        return new ResetPasswordResponse(password, sent);
    }

    private async Task<Error?> ValidateRolesAsync(IReadOnlyList<Guid> roleIds, UserType userType, CancellationToken ct)
    {
        var roles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Code).ToListAsync(ct);
        if (roles.Count != roleIds.Distinct().Count())
        {
            return Error.Validation("ROLE_NOT_FOUND");
        }

        var supplierRoles = roles.Count(c => c.StartsWith("SUPPLIER_", StringComparison.Ordinal));
        return userType == UserType.Supplier && supplierRoles != roles.Count
            || userType == UserType.Internal && supplierRoles > 0
            ? Error.Validation("ROLE_USER_TYPE_MISMATCH")
            : null;
    }

    private async Task<bool> SendPasswordEmailAsync(User user, string password, bool isNew, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return false;
        }

        var loginUrl = links.Build("/dang-nhap");
        var intro = isNew ? "Tài khoản của bạn trên Cổng mua sắm trực tuyến đã được tạo." : "Mật khẩu tài khoản của bạn đã được đặt lại.";
        var html = $"""
            <p>Xin chào {WebUtility.HtmlEncode(user.FullName)},</p>
            <p>{intro}</p>
            <ul>
              <li>Tên đăng nhập: <b>{WebUtility.HtmlEncode(user.UserName)}</b></li>
              <li>Mật khẩu tạm thời: <b>{WebUtility.HtmlEncode(password)}</b></li>
            </ul>
            <p>Vui lòng đăng nhập tại <a href="{loginUrl}">{loginUrl}</a> và đổi mật khẩu ngay.</p>
            """;
        await email.SendAsync(new EmailMessage([user.Email], isNew ? "Tài khoản Cổng mua sắm trực tuyến" : "Đặt lại mật khẩu", html), ct);
        return true;
    }

    private UserDetailDto ToDetail(User u) => new(
        u.Id,
        u.UserName,
        u.FullName,
        u.Email,
        u.Phone,
        u.JobTitle,
        u.UserType,
        u.Status,
        u.DepartmentId,
        u.FacilityId,
        u.SupplierId,
        [.. u.Roles.Select(r => r.RoleId)],
        [.. u.DataScopes.Select(s => new DataScopeDto(s.ScopeType, s.Value))],
        u.CertificateSerial,
        u.CertificateIssuer,
        u.CertificateValidTo,
        u.IsLockedOut(clock.UtcNow),
        u.TwoFactorEnabled,
        u.MustChangePassword,
        u.LastLoginAt);
}

// ---------- Supplier portal: company account & staff (SUPPLIER_ADMIN) ----------

public sealed record SupplierStaffDto(Guid Id, string UserName, string FullName, string? Email, UserStatus Status, bool IsAdmin, DateTimeOffset? LastLoginAt);

public sealed record CreateSupplierStaffRequest(string UserName, string FullName, string Email, bool IsAdmin);

public sealed record UpdateSupplierStaffRequest(UserStatus Status, bool IsAdmin);

internal sealed class CreateSupplierStaffRequestValidator : AbstractValidator<CreateSupplierStaffRequest>
{
    public CreateSupplierStaffRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithName("Tên đăng nhập")
            .Matches("^[A-Za-z0-9._-]{3,64}$").WithMessage("Tên đăng nhập 3–64 ký tự, chỉ gồm chữ, số, dấu chấm, gạch dưới, gạch ngang.");
        RuleFor(x => x.FullName).NotEmpty().WithName("Họ tên");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithName("Email");
    }
}

/// <summary>
/// Supplier admins manage their own company's staff. Isolation comes from the global query filter:
/// every query here only ever sees users of the caller's supplier.
/// </summary>
public sealed class SupplierCompanyService(IAppDbContext db, ICurrentUser current, SupplierService suppliers)
{
    public async Task<Result<SupplierDto>> GetMyCompanyAsync(CancellationToken ct) =>
        current.SupplierId is { } id ? await suppliers.GetAsync(id, ct) : Error.Forbidden();

    public async Task<Result<List<SupplierStaffDto>>> ListStaffAsync(CancellationToken ct)
    {
        if (current.SupplierId is not { } supplierId)
        {
            return Error.Forbidden();
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.SupplierId == supplierId)
            .OrderBy(u => u.UserName)
            .Select(u => new SupplierStaffDto(u.Id, u.UserName, u.FullName, u.Email, u.Status,
                u.Roles.Any(r => r.Role!.Code == RoleCatalog.SupplierAdmin), u.LastLoginAt))
            .ToListAsync(ct);
    }

    public async Task<Result<Guid>> CreateStaffAsync(CreateSupplierStaffRequest r, CancellationToken ct) =>
        current.SupplierId is { } supplierId
            ? await suppliers.CreateSupplierAccountAsync(supplierId, new SupplierAccountRequest(r.UserName, r.FullName, r.Email), r.IsAdmin, ct)
            : Error.Forbidden();

    public async Task<Result> UpdateStaffAsync(Guid userId, UpdateSupplierStaffRequest r, CancellationToken ct)
    {
        if (current.SupplierId is null)
        {
            return Result.Failure(Error.Forbidden());
        }

        if (userId == current.UserId && (r.Status != UserStatus.Active || !r.IsAdmin))
        {
            return Result.Failure(Error.Validation("CANNOT_DEMOTE_SELF"));
        }

        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Failure(Error.NotFound());
        }

        var roleCode = r.IsAdmin ? RoleCatalog.SupplierAdmin : RoleCatalog.SupplierUser;
        var roleId = await db.Roles.Where(x => x.Code == roleCode).Select(x => x.Id).FirstAsync(ct);
        user.SetStatus(r.Status);
        user.SetRoles([roleId]);
        user.RotateSecurityStamp();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
