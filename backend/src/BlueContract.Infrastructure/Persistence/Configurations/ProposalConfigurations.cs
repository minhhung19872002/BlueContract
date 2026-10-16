using BlueContract.Domain.Common;
using BlueContract.Domain.Proposals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("number_sequences");
        b.HasKey(x => new { x.Prefix, x.Year });
        b.Property(x => x.Prefix).HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> b)
    {
        b.ToTable("proposals");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.PeriodText).HasMaxLength(100).IsRequired();
        b.Property(x => x.Note).HasMaxLength(2000);
        b.Property(x => x.CreatedByName).HasMaxLength(200);
        b.HasIndex(x => new { x.Domain, x.Status });
        b.HasIndex(x => new { x.DepartmentId, x.Status });
        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.SummaryId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ProposalId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.IsEditable);
    }
}

internal sealed class ProposalLineConfiguration : IEntityTypeConfiguration<ProposalLine>
{
    public void Configure(EntityTypeBuilder<ProposalLine> b)
    {
        b.ToTable("proposal_lines");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ContractNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.ItemCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        b.Property(x => x.HisKey).HasMaxLength(60);
        b.Property(x => x.Note).HasMaxLength(500);
        foreach (var p in new[] { nameof(ProposalLine.OpeningStock), nameof(ProposalLine.Received), nameof(ProposalLine.Issued), nameof(ProposalLine.CurrentStock), nameof(ProposalLine.ContractRemaining), nameof(ProposalLine.ProposedQuantity) })
        {
            b.Property<decimal>(p).HasPrecision(18, 3);
        }

        b.HasIndex(x => x.ContractItemId);
    }
}

internal sealed class ProposalSummaryConfiguration : IEntityTypeConfiguration<ProposalSummary>
{
    public void Configure(EntityTypeBuilder<ProposalSummary> b)
    {
        b.ToTable("proposal_summaries");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.PeriodText).HasMaxLength(100).IsRequired();
        b.Property(x => x.AggregatorName).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(2000);
        b.HasIndex(x => new { x.Domain, x.Status });
        b.HasIndex(x => x.DocumentId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SummaryId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Sources).WithOne().HasForeignKey(s => s.SummaryId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.IsEditable);
    }
}

internal sealed class ProposalSummarySourceConfiguration : IEntityTypeConfiguration<ProposalSummarySource>
{
    public void Configure(EntityTypeBuilder<ProposalSummarySource> b)
    {
        b.ToTable("proposal_summary_sources");
        b.HasKey(x => new { x.SummaryId, x.ProposalId });
        b.HasIndex(x => x.ProposalId);
    }
}

internal sealed class ProposalSummaryLineConfiguration : IEntityTypeConfiguration<ProposalSummaryLine>
{
    public void Configure(EntityTypeBuilder<ProposalSummaryLine> b)
    {
        b.ToTable("proposal_summary_lines");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ContractNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.SupplierName).HasMaxLength(300).IsRequired();
        b.Property(x => x.ItemCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        foreach (var p in new[]
                 {
                     nameof(ProposalSummaryLine.ProposedQuantity), nameof(ProposalSummaryLine.OpeningStock), nameof(ProposalSummaryLine.Received),
                     nameof(ProposalSummaryLine.Issued), nameof(ProposalSummaryLine.CurrentStock), nameof(ProposalSummaryLine.ContractRemaining),
                     nameof(ProposalSummaryLine.ApprovedQuantity), nameof(ProposalSummaryLine.CalledQuantity),
                 })
        {
            b.Property<decimal>(p).HasPrecision(18, 3);
        }

        b.HasIndex(x => x.ContractItemId);
        b.Ignore(x => x.RemainingToCall);
        b.Ignore(x => x.Amount);
    }
}

internal sealed class CallOrderConfiguration : IEntityTypeConfiguration<CallOrder>
{
    public void Configure(EntityTypeBuilder<CallOrder> b)
    {
        b.ToTable("call_orders");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.ContactName).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(2000);
        b.Property(x => x.ConfirmedByName).HasMaxLength(200);
        b.Property(x => x.SupplierNote).HasMaxLength(2000);
        b.HasIndex(x => new { x.SupplierId, x.Status });
        b.HasIndex(x => new { x.Status, x.DeliveryDeadline });
        b.HasIndex(x => x.SummaryId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.CallOrderId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.TotalAmount);
    }
}

internal sealed class CallOrderLineConfiguration : IEntityTypeConfiguration<CallOrderLine>
{
    public void Configure(EntityTypeBuilder<CallOrderLine> b)
    {
        b.ToTable("call_order_lines");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ContractNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.ItemCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        b.Property(x => x.ShortageReason).HasMaxLength(1000);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.ConfirmedQuantity).HasPrecision(18, 3);
        b.Property(x => x.DeliveredQuantity).HasPrecision(18, 3);
        b.HasIndex(x => x.ContractItemId);
        b.HasIndex(x => x.SummaryLineId);
        b.Ignore(x => x.Amount);
    }
}
