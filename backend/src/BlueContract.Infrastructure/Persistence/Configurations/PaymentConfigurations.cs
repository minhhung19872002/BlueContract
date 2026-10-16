using BlueContract.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class AcceptanceRecordConfiguration : IEntityTypeConfiguration<AcceptanceRecord>
{
    public void Configure(EntityTypeBuilder<AcceptanceRecord> b)
    {
        b.ToTable("acceptance_records");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.PeriodText).HasMaxLength(100).IsRequired();
        b.Property(x => x.Conclusion).HasMaxLength(2000);
        b.Property(x => x.LastReturnReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.ContractId, x.Status });
        b.HasIndex(x => new { x.SupplierId, x.Status });
        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.ValuationId);
        b.HasMany(x => x.Receipts).WithOne().HasForeignKey(r => r.AcceptanceId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.AcceptanceId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.IsEditable);
    }
}

internal sealed class AcceptanceReceiptConfiguration : IEntityTypeConfiguration<AcceptanceReceipt>
{
    public void Configure(EntityTypeBuilder<AcceptanceReceipt> b)
    {
        b.ToTable("acceptance_receipts");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(60).IsRequired();
        b.Property(x => x.InvoiceNumber).HasMaxLength(20);
        b.HasIndex(x => x.ReceiptId);
    }
}

internal sealed class AcceptanceItemConfiguration : IEntityTypeConfiguration<AcceptanceItem>
{
    public void Configure(EntityTypeBuilder<AcceptanceItem> b)
    {
        b.ToTable("acceptance_items");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ItemCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.HasIndex(x => x.ContractItemId);
    }
}

internal sealed class ValuationRecordConfiguration : IEntityTypeConfiguration<ValuationRecord>
{
    public void Configure(EntityTypeBuilder<ValuationRecord> b)
    {
        b.ToTable("valuation_records");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.PaymentRequestNumber).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.PaymentRequestNumber).IsUnique();
        b.Property(x => x.PeriodText).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastReturnReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.ContractId, x.Status });
        b.HasIndex(x => new { x.SupplierId, x.PaymentStatus });
        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.PaymentRequestDocumentId);
        b.HasIndex(x => x.LiquidationId);
        b.HasMany(x => x.Acceptances).WithOne().HasForeignKey(a => a.ValuationId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ValuationId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.PaymentOrders).WithOne().HasForeignKey(p => p.ValuationId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.CumulativeToDate);
        b.Ignore(x => x.CompletionPercent);
        b.Ignore(x => x.RequestedAmount);
        b.Ignore(x => x.PaidAmount);
        b.Ignore(x => x.IsEditable);
    }
}

internal sealed class ValuationAcceptanceConfiguration : IEntityTypeConfiguration<ValuationAcceptance>
{
    public void Configure(EntityTypeBuilder<ValuationAcceptance> b)
    {
        b.ToTable("valuation_acceptances");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.Property(x => x.PeriodText).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.AcceptanceId);
    }
}

internal sealed class ValuationItemConfiguration : IEntityTypeConfiguration<ValuationItem>
{
    public void Configure(EntityTypeBuilder<ValuationItem> b)
    {
        b.ToTable("valuation_items");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        b.Property(x => x.ContractQuantity).HasPrecision(18, 3);
        b.Property(x => x.PreviousQuantity).HasPrecision(18, 3);
        b.Property(x => x.CurrentQuantity).HasPrecision(18, 3);
        b.HasIndex(x => x.ContractItemId);
        b.Ignore(x => x.CumulativeQuantity);
        b.Ignore(x => x.ContractAmount);
        b.Ignore(x => x.PreviousAmount);
        b.Ignore(x => x.CurrentAmount);
        b.Ignore(x => x.CumulativeAmount);
    }
}

internal sealed class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> b)
    {
        b.ToTable("payment_orders");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.Property(x => x.FileKey).HasMaxLength(500);
        b.Property(x => x.FileName).HasMaxLength(255);
        b.Property(x => x.AddedByName).HasMaxLength(200);
    }
}

internal sealed class LiquidationRecordConfiguration : IEntityTypeConfiguration<LiquidationRecord>
{
    public void Configure(EntityTypeBuilder<LiquidationRecord> b)
    {
        b.ToTable("liquidation_records");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.LastReturnReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.ContractId, x.Status });
        b.HasIndex(x => x.SupplierId);
        b.HasIndex(x => x.DocumentId);
        b.HasMany(x => x.Valuations).WithOne().HasForeignKey(v => v.LiquidationId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Settlements).WithOne().HasForeignKey(s => s.LiquidationId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.SettledAmount);
        b.Ignore(x => x.RemainingAmount);
        b.Ignore(x => x.ReductionAmount);
        b.Ignore(x => x.IsEditable);
    }
}

internal sealed class LiquidationValuationConfiguration : IEntityTypeConfiguration<LiquidationValuation>
{
    public void Configure(EntityTypeBuilder<LiquidationValuation> b)
    {
        b.ToTable("liquidation_valuations");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.Property(x => x.PaymentOrderNumbers).HasMaxLength(500);
        b.HasIndex(x => x.ValuationId);
    }
}

internal sealed class LiquidationSettlementConfiguration : IEntityTypeConfiguration<LiquidationSettlement>
{
    public void Configure(EntityTypeBuilder<LiquidationSettlement> b)
    {
        b.ToTable("liquidation_settlements");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.VoucherNumber).HasMaxLength(50);
        b.Property(x => x.FileKey).HasMaxLength(500);
        b.Property(x => x.FileName).HasMaxLength(255);
        b.Property(x => x.MarkedByName).HasMaxLength(200);
    }
}
