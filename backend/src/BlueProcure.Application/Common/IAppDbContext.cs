using BlueProcure.Domain.Common;
using BlueProcure.Domain.Identity;
using BlueProcure.Domain.Organizations;
using BlueProcure.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace BlueProcure.Application.Common;

/// <summary>Unit of work over the application database. Use cases query through these sets (with projections).</summary>
public interface IAppDbContext
{
    DbSet<SystemSetting> SystemSettings { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<Facility> Facilities { get; }

    DbSet<Department> Departments { get; }

    DbSet<Warehouse> Warehouses { get; }

    DbSet<Supplier> Suppliers { get; }

    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<UserRole> UserRoles { get; }

    DbSet<RolePermission> RolePermissions { get; }

    DbSet<UserDataScope> UserDataScopes { get; }

    DbSet<SigningDelegation> SigningDelegations { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<DepartmentHistory> DepartmentHistories { get; }

    DbSet<UserHistory> UserHistories { get; }

    DbSet<SupplierHistory> SupplierHistories { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
