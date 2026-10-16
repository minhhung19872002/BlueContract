using System.Text.Json;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Integration;
using BlueContract.Domain.Receipts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseReceiptConfiguration : IEntityTypeConfiguration<WarehouseReceipt>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<WarehouseReceipt> b)
    {
        b.ToTable("warehouse_receipts");
        b.Property(x => x.Number).HasMaxLength(60).IsRequired();
        b.Property(x => x.HisReceiptId).HasMaxLength(100);
        b.Property(x => x.HisPayloadHash).HasMaxLength(64);
        b.Property(x => x.WarehouseCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.SupplierTaxCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.BidDecisionNumber).HasMaxLength(100);
        b.Property(x => x.ContractNumber).HasMaxLength(100);
        b.Property(x => x.DelivererName).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(2000);
        b.Property(x => x.InvoiceSymbol).HasMaxLength(20);
        b.Property(x => x.InvoiceNumber).HasMaxLength(20);
        b.Property(x => x.InvoiceSellerTaxCode).HasMaxLength(20);
        b.Property(x => x.InvoiceXmlFileKey).HasMaxLength(500);
        b.Property(x => x.InvoicePdfFileKey).HasMaxLength(500);
        b.Property(x => x.InvoiceUpdatedByName).HasMaxLength(200);
        b.Property(x => x.LastReturnReason).HasMaxLength(2000);

        // Validation engine output, kept as JSONB on the receipt (read with the receipt, never queried by content).
        b.Property<List<ReceiptIssue>>("_issues")
            .HasColumnName("issues")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<ReceiptIssue>>(v, Json) ?? new List<ReceiptIssue>(),
                new ValueComparer<List<ReceiptIssue>>((a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal), v => v.ToList()))
            .IsRequired();
        b.Ignore(x => x.Issues);

        // Idempotency key of the HIS (spec §6.4 B1).
        b.HasIndex(x => x.HisReceiptId).IsUnique().HasFilter("his_receipt_id IS NOT NULL");
        b.HasIndex(x => x.Number);

        // Numbers made by the portal's counter are unique; HIS numbers are the HIS's own (one counter per HIS warehouse).
        b.HasIndex(x => x.Number, "ux_warehouse_receipts_portal_number").IsUnique().HasDatabaseName("ux_warehouse_receipts_portal_number").HasFilter("source <> 'His'");
        b.HasIndex(x => new { x.ContractId, x.Status });
        b.HasIndex(x => new { x.SupplierId, x.Status });
        b.HasIndex(x => new { x.Status, x.ValidationStatus });
        b.HasIndex(x => x.ReceiptDate);
        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.CallOrderId);
        b.HasIndex(x => new { x.SupplierTaxCode, x.InvoiceSymbol, x.InvoiceNumber });

        // One invoice backs at most one receipt that is not cancelled and not invalid (a duplicate is stored but Invalid).
        b.HasIndex(x => new { x.SupplierTaxCode, x.InvoiceSymbol, x.InvoiceNumber }, "ux_warehouse_receipts_invoice_active").IsUnique().HasDatabaseName("ux_warehouse_receipts_invoice_active")
            .HasFilter("invoice_number IS NOT NULL AND status <> 'Cancelled' AND validation_status <> 'Invalid'");
        b.HasIndex(x => x.AcceptanceId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.HasInvoice);
        b.Ignore(x => x.IsVirtual);
        b.Ignore(x => x.TotalAmount);
        b.Ignore(x => x.VatAmount);
        b.Ignore(x => x.SignedAmount);
        b.Ignore(x => x.IsEditable);
        b.Ignore(x => x.CountsAsReceived);
    }
}

internal sealed class WarehouseReceiptLineConfiguration : IEntityTypeConfiguration<WarehouseReceiptLine>
{
    public void Configure(EntityTypeBuilder<WarehouseReceiptLine> b)
    {
        b.ToTable("warehouse_receipt_lines");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.HisKey).HasMaxLength(60);
        b.Property(x => x.ItemCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ItemName).HasMaxLength(500).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(40).IsRequired();
        b.Property(x => x.LotNumber).HasMaxLength(60);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.VatRate).HasPrecision(5, 2);
        b.HasIndex(x => x.ContractItemId);
        b.HasIndex(x => new { x.ReceiptId, x.LineNo }).IsUnique();
    }
}

internal sealed class IntegrityCheckRunConfiguration : IEntityTypeConfiguration<IntegrityCheckRun>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<IntegrityCheckRun> b)
    {
        b.ToTable("integrity_check_runs");
        b.Property(x => x.Trigger).HasMaxLength(200).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2000);
        b.Property<List<IntegrityIssue>>("_issues")
            .HasColumnName("issues")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<IntegrityIssue>>(v, Json) ?? new List<IntegrityIssue>(),
                new ValueComparer<List<IntegrityIssue>>((a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal), v => v.ToList()))
            .IsRequired();
        b.Ignore(x => x.Issues);
        b.Ignore(x => x.IsHealthy);
        b.HasIndex(x => x.StartedAt);
    }
}

internal sealed class IntegrationLogConfiguration : IEntityTypeConfiguration<IntegrationLog>
{
    public void Configure(EntityTypeBuilder<IntegrationLog> b)
    {
        b.ToTable("integration_logs");
        b.Property(x => x.System).HasMaxLength(20).IsRequired();
        b.Property(x => x.Operation).HasMaxLength(60).IsRequired();
        b.Property(x => x.Method).HasMaxLength(10).IsRequired();
        b.Property(x => x.Path).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.RemoteIp).HasMaxLength(64);
        b.Property(x => x.ClientName).HasMaxLength(100);
        b.HasIndex(x => x.StartedAt);
        b.HasIndex(x => new { x.Status, x.StartedAt });
        b.HasIndex(x => x.ExternalId);
        b.Ignore(x => x.CanRetry);
    }
}

internal sealed class UsedRequestSignatureConfiguration : IEntityTypeConfiguration<UsedRequestSignature>
{
    public void Configure(EntityTypeBuilder<UsedRequestSignature> b)
    {
        b.ToTable("used_request_signatures");
        b.HasKey(x => x.Signature);
        b.Property(x => x.Signature).HasMaxLength(128);
        b.HasIndex(x => x.ReceivedAt);
    }
}
