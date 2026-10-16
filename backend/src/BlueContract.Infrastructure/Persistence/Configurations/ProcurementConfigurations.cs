using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json.Serialization;
using System.Text.Json;
using BlueContract.Domain.Bids;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal static class Precision
{
    /// <summary>Quantities: decimal(18,3) (spec §15).</summary>
    public static PropertyBuilder<decimal> Quantity(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 3);

    public static PropertyBuilder<decimal?> Quantity(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 3);
}

internal sealed class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> b)
    {
        b.ToTable("item_categories");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.DefaultContractTemplateCode).HasMaxLength(60);
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.Property(x => x.HisKey).HasMaxLength(60);
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(500).IsRequired();
        b.Property(x => x.ActiveIngredient).HasMaxLength(1000);
        b.Property(x => x.Strength).HasMaxLength(200);
        b.Property(x => x.Specification).HasMaxLength(300);
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        b.Property(x => x.Manufacturer).HasMaxLength(300);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.RegistrationNumber).HasMaxLength(100);
        b.Property(x => x.BidGroup).HasMaxLength(40);
        b.HasIndex(x => new { x.BidDecisionId, x.SupplierId, x.Code });

        // Catalogue items outside a bid: one code per supplier (null supplier counts as one value).
        b.HasIndex(x => new { x.Code, x.SupplierId }, "ux_items_free_code").IsUnique().HasDatabaseName("ux_items_free_code").AreNullsDistinct(false)
            .HasFilter("bid_decision_id IS NULL AND is_deleted = false");
        b.HasIndex(x => x.HisKey);
        b.HasIndex(x => new { x.CategoryId, x.IsActive });
        b.HasIndex(x => x.SupplierId);
        b.HasOne<ItemCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BidDecisionConfiguration : IEntityTypeConfiguration<BidDecision>
{
    public void Configure(EntityTypeBuilder<BidDecision> b)
    {
        b.ToTable("bid_decisions");
        b.Property(x => x.Number).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Number).IsUnique().HasFilter("is_deleted = false");
        b.Property(x => x.Summary).HasMaxLength(2000).IsRequired();
        b.Property(x => x.PackageName).HasMaxLength(500).IsRequired();
        b.Property(x => x.SelectionMethod).HasMaxLength(200);
        b.Property(x => x.FundingSource).HasMaxLength(300);
        b.Property(x => x.ExecutionPeriod).HasMaxLength(200);
        b.Property(x => x.AttachmentKey).HasMaxLength(300);
        b.Property(x => x.AttachmentName).HasMaxLength(300);
        b.Property(x => x.AttachmentSha256).HasMaxLength(64);
        b.HasIndex(x => new { x.SignedDate, x.Status });
        b.HasOne<ItemCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Suppliers).WithOne().HasForeignKey(s => s.BidDecisionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.BidDecisionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BidDecisionSupplierConfiguration : IEntityTypeConfiguration<BidDecisionSupplier>
{
    public void Configure(EntityTypeBuilder<BidDecisionSupplier> b)
    {
        b.ToTable("bid_decision_suppliers");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.BidDecisionId, x.SupplierId }).IsUnique();
        b.HasIndex(x => x.SupplierId);
    }
}

internal sealed class BidDecisionItemConfiguration : IEntityTypeConfiguration<BidDecisionItem>
{
    public void Configure(EntityTypeBuilder<BidDecisionItem> b)
    {
        b.ToTable("bid_decision_items");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Quantity).Quantity();
        b.HasIndex(x => new { x.BidDecisionId, x.ItemId }).IsUnique();
        b.HasIndex(x => new { x.BidDecisionId, x.SupplierId });
        b.Ignore(x => x.Amount);
    }
}

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> b)
    {
        b.ToTable("contracts");
        b.Property(x => x.Number).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Number).IsUnique().HasFilter("is_deleted = false");
        b.Property(x => x.Name).HasMaxLength(500).IsRequired();
        b.Property(x => x.Duration).HasMaxLength(300);
        b.Property(x => x.PaymentTerms).HasMaxLength(4000);
        b.Property(x => x.WarrantyTerms).HasMaxLength(4000);
        b.Property(x => x.DeliveryTerms).HasMaxLength(4000);
        b.Property(x => x.GuaranteeNumber).HasMaxLength(100);
        b.Property(x => x.GuaranteeBank).HasMaxLength(300);
        b.Property(x => x.Note).HasMaxLength(2000);
        b.Property(x => x.LastReturnReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.SupplierId, x.Status });
        b.HasIndex(x => new { x.Status, x.ExpiryDate });
        b.HasIndex(x => x.BidDecisionId);
        b.HasIndex(x => x.CategoryId);
        b.HasIndex(x => x.DocumentId);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ContractId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.IsEditable);
        b.Ignore(x => x.IsEffective);
        b.Ignore(x => x.SingleFacilityId);
        b.Property(x => x.FacilityIds).HasColumnType("uuid[]");
    }
}

internal sealed class ContractItemConfiguration : IEntityTypeConfiguration<ContractItem>
{
    public void Configure(EntityTypeBuilder<ContractItem> b)
    {
        b.ToTable("contract_items");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ItemCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        b.Property(x => x.Quantity).Quantity();
        b.Property(x => x.ProposedQuantity).Quantity();
        b.Property(x => x.ApprovedQuantity).Quantity();
        b.Property(x => x.CalledQuantity).Quantity();
        b.Property(x => x.ReceivedQuantity).Quantity();
        b.Property(x => x.AcceptedQuantity).Quantity();
        b.Property(x => x.PaidQuantity).Quantity();
        b.Property(x => x.VatRate).HasPrecision(5, 2);
        b.HasIndex(x => new { x.ContractId, x.ItemId }).IsUnique();
        b.HasIndex(x => x.ItemId);
        b.Ignore(x => x.RemainingQuantity);
    }
}

internal sealed class ContractAppendixConfiguration : IEntityTypeConfiguration<ContractAppendix>
{
    private static readonly JsonSerializerOptions InfoJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<ContractAppendix> b)
    {
        b.ToTable("contract_appendices");
        b.Property(x => x.Number).HasMaxLength(100).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        b.Property(x => x.InfoChanges).HasMaxLength(4000);

        // "Thay đổi thông tin": field changes old → new, read with the appendix, never queried by content.
        b.Property(x => x.InfoFields)
            .HasColumnName("info_fields")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, InfoJson),
                v => JsonSerializer.Deserialize<List<AppendixInfoChange>>(v, InfoJson) ?? new List<AppendixInfoChange>(),
                new ValueComparer<IReadOnlyList<AppendixInfoChange>>((a, c) => JsonSerializer.Serialize(a, InfoJson) == JsonSerializer.Serialize(c, InfoJson),
                    v => JsonSerializer.Serialize(v, InfoJson).GetHashCode(StringComparison.Ordinal), v => v.ToList()))
            .IsRequired();
        b.HasIndex(x => new { x.ContractId, x.Number }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => x.ContractId, "ux_contract_appendices_contract_open").IsUnique().HasDatabaseName("ux_contract_appendices_contract_open").HasFilter("status IN ('Draft', 'InWorkflow') AND is_deleted = false");
        b.HasIndex(x => x.DocumentId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.AppendixId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContractAppendixLineConfiguration : IEntityTypeConfiguration<ContractAppendixLine>
{
    public void Configure(EntityTypeBuilder<ContractAppendixLine> b)
    {
        b.ToTable("contract_appendix_lines");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.OldQuantity).Quantity();
        b.Property(x => x.NewQuantity).Quantity();
    }
}
