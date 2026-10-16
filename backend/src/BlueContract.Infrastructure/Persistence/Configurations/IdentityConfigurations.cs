using BlueContract.Domain.Common;
using BlueContract.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.Property(x => x.UserName).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.UserName).IsUnique();
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.JobTitle).HasMaxLength(200);
        b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
        b.Property(x => x.TwoFactorSecret).HasMaxLength(1000);
        b.Property(x => x.CertificateSerial).HasMaxLength(100);
        b.Property(x => x.CertificateSubject).HasMaxLength(1000);
        b.Property(x => x.CertificateIssuer).HasMaxLength(300);
        b.Property(x => x.SignatureImageFileKey).HasMaxLength(500);
        b.Property(x => x.InitialImageFileKey).HasMaxLength(500);
        b.HasIndex(x => x.SupplierId);
        b.HasIndex(x => x.DepartmentId);
        b.Ignore(x => x.IsActive);

        b.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.DataScopes).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.DataScopes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles");
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Ignore(x => x.IsSupplierRole);
        b.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions");
        b.HasKey(x => new { x.RoleId, x.PermissionCode });
        b.Property(x => x.PermissionCode).HasMaxLength(100);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles");
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserDataScopeConfiguration : IEntityTypeConfiguration<UserDataScope>
{
    public void Configure(EntityTypeBuilder<UserDataScope> b)
    {
        b.ToTable("user_data_scopes");
        b.Property(x => x.Value).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.UserId, x.ScopeType, x.Value }).IsUnique();
    }
}

internal sealed class SigningDelegationConfiguration : IEntityTypeConfiguration<SigningDelegation>
{
    public void Configure(EntityTypeBuilder<SigningDelegation> b)
    {
        b.ToTable("signing_delegations");
        b.Property(x => x.DelegatorTitle).HasMaxLength(100).IsRequired();
        b.Property(x => x.DelegateTitle).HasMaxLength(100).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => new { x.DelegatorUserId, x.ValidFrom });
        b.HasIndex(x => x.DelegateUserId);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => x.UserId);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.ReplacedByHash).HasMaxLength(128);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Action).HasMaxLength(64).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.UserName).HasMaxLength(64);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(500);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.OldValues).HasColumnType("jsonb");
        b.Property(x => x.NewValues).HasColumnType("jsonb");
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => new { x.UserId, x.OccurredAt });
    }
}

internal sealed class DepartmentHistoryConfiguration : IEntityTypeConfiguration<DepartmentHistory>
{
    public void Configure(EntityTypeBuilder<DepartmentHistory> b) => HistoryMapping.Map(b, "department_history");
}

internal sealed class UserHistoryConfiguration : IEntityTypeConfiguration<UserHistory>
{
    public void Configure(EntityTypeBuilder<UserHistory> b) => HistoryMapping.Map(b, "user_history");
}

internal sealed class SupplierHistoryConfiguration : IEntityTypeConfiguration<SupplierHistory>
{
    public void Configure(EntityTypeBuilder<SupplierHistory> b) => HistoryMapping.Map(b, "supplier_history");
}

internal static class HistoryMapping
{
    public static void Map<T>(EntityTypeBuilder<T> b, string table)
        where T : HistoryRecord
    {
        b.ToTable(table);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Snapshot).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ChangedByName).HasMaxLength(200);
        b.HasIndex(x => new { x.EntityId, x.Version }).IsUnique();
    }
}
