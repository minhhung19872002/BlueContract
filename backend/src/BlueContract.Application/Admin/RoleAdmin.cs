using BlueContract.Application.Common;
using BlueContract.Domain.Identity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Admin;

public sealed record RoleDto(Guid Id, string Code, string Name, string? Description, bool IsSystem, bool IsSupplierRole, int UserCount, IReadOnlyList<string> Permissions);

public sealed record PermissionDto(string Code, string Name, string Group);

public sealed record PermissionMatrixDto(IReadOnlyList<PermissionDto> Permissions, IReadOnlyList<RoleDto> Roles);

public sealed record SaveRoleRequest(string Code, string Name, string? Description);

public sealed record SetRolePermissionsRequest(IReadOnlyList<string> Permissions);

internal sealed class SaveRoleRequestValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("Mã vai trò").Matches("^[A-Za-z][A-Za-z0-9_]{1,49}$")
            .WithMessage("Mã vai trò bắt đầu bằng chữ cái, chỉ gồm chữ, số và gạch dưới.");
        RuleFor(x => x.Name).NotEmpty().WithName("Tên vai trò").MaximumLength(200);
    }
}

public sealed class RoleService(IAppDbContext db)
{
    public async Task<PermissionMatrixDto> GetMatrixAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking()
            .OrderBy(r => r.Code)
            .Select(r => new RoleDto(
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.IsSystem,
                r.Code.StartsWith("SUPPLIER_"),
                db.UserRoles.Count(ur => ur.RoleId == r.Id),
                r.Permissions.Select(p => p.PermissionCode).ToList()))
            .ToListAsync(ct);
        var permissions = PermissionCatalog.All.Select(p => new PermissionDto(p.Code, p.Name, p.Group)).ToList();
        return new PermissionMatrixDto(permissions, roles);
    }

    public async Task<Result<Guid>> CreateAsync(SaveRoleRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Roles.IgnoreQueryFilters().AnyAsync(x => x.Code == code, ct))
        {
            return Error.Conflict("ROLE_CODE_EXISTS");
        }

        var role = new Role(code, r.Name, r.Description, isSystem: false);
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, SaveRoleRequest r, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
        {
            return Result.Failure(Error.NotFound());
        }

        role.Update(r.Name, r.Description);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetPermissionsAsync(Guid id, SetRolePermissionsRequest r, CancellationToken ct)
    {
        var role = await db.Roles.Include(x => x.Permissions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
        {
            return Result.Failure(Error.NotFound());
        }

        role.SetPermissions(r.Permissions);

        // Members must pick up the new permission set on their next request.
        var members = await db.Users.Where(u => u.Roles.Any(ur => ur.RoleId == id)).ToListAsync(ct);
        members.ForEach(u => u.RotateSecurityStamp());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (role.IsSystem)
        {
            return Result.Failure(Error.Validation("ROLE_SYSTEM_CANNOT_DELETE"));
        }

        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id, ct))
        {
            return Result.Failure(Error.Validation("ROLE_IN_USE"));
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
