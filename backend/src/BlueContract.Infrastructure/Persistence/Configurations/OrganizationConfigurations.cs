using BlueContract.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("organizations");
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.ShortName).HasMaxLength(100);
        b.Property(x => x.TaxCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.RepresentativeName).HasMaxLength(200);
        b.Property(x => x.RepresentativeTitle).HasMaxLength(200);
        b.Property(x => x.BankAccountNumber).HasMaxLength(50);
        b.Property(x => x.BankName).HasMaxLength(300);
        b.Property(x => x.TreasuryAccountNumber).HasMaxLength(50);
        b.Property(x => x.TreasuryName).HasMaxLength(300);
        b.Property(x => x.LogoFileKey).HasMaxLength(500);
        b.Property(x => x.SealImageFileKey).HasMaxLength(500);
    }
}

internal sealed class FacilityConfiguration : IEntityTypeConfiguration<Facility>
{
    public void Configure(EntityTypeBuilder<Facility> b)
    {
        b.ToTable("facilities");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.HisFacilityCode).HasMaxLength(50);
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("departments");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.HisDepartmentCode).HasMaxLength(50);
        b.HasOne(x => x.Facility).WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.FacilityId);
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.HisWarehouseCode).HasMaxLength(50);
        b.HasIndex(x => x.HisWarehouseCode);
        b.HasOne(x => x.Facility).WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.Property(x => x.TaxCode).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.TaxCode).IsUnique();
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.ShortName).HasMaxLength(100);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.RepresentativeName).HasMaxLength(200);
        b.Property(x => x.RepresentativeTitle).HasMaxLength(200);
        b.Property(x => x.AuthorizationDocument).HasMaxLength(300);
        b.Property(x => x.BankAccountNumber).HasMaxLength(50);
        b.Property(x => x.BankName).HasMaxLength(300);
        b.Property(x => x.NotificationEmail).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(30);
    }
}
