using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowTemplateConfiguration : IEntityTypeConfiguration<WorkflowTemplate>
{
    public void Configure(EntityTypeBuilder<WorkflowTemplate> b)
    {
        b.ToTable("workflow_templates");
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.Condition).HasMaxLength(300);
        b.HasIndex(x => new { x.DocumentType, x.IsActive });
        b.HasMany<WorkflowStepTemplate>("_steps").WithOne().HasForeignKey(s => s.TemplateId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.Steps);
    }
}

internal sealed class WorkflowStepTemplateConfiguration : IEntityTypeConfiguration<WorkflowStepTemplate>
{
    public void Configure(EntityTypeBuilder<WorkflowStepTemplate> b)
    {
        b.ToTable("workflow_step_templates");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.RoleCode).HasMaxLength(50);
        b.Property(x => x.ReturnTo).HasMaxLength(100).IsRequired();
        b.Property(x => x.SignatureAnchor).HasMaxLength(80);
        b.Ignore(x => x.ReturnToOrders);
        b.Ignore(x => x.RequiresSignature);
        b.HasIndex(x => new { x.TemplateId, x.Order });
    }
}

internal sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> b)
    {
        b.ToTable("workflow_instances");
        b.Property(x => x.TemplateCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.StartedByName).HasMaxLength(200);
        b.Property(x => x.ScopeDomain).HasMaxLength(40);
        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => new { x.Status, x.SupplierId });
        b.HasIndex(x => x.StartedBy);
        b.HasMany<WorkflowStepInstance>("_steps").WithOne().HasForeignKey(s => s.InstanceId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany<WorkflowEvent>("_events").WithOne().HasForeignKey(e => e.InstanceId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.Steps);
        b.Ignore(x => x.Events);
        b.Ignore(x => x.ActiveStep);
    }
}

internal sealed class WorkflowStepInstanceConfiguration : IEntityTypeConfiguration<WorkflowStepInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowStepInstance> b)
    {
        b.ToTable("workflow_steps");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.RoleCode).HasMaxLength(50);
        b.Property(x => x.ReturnTo).HasMaxLength(100).IsRequired();
        b.Property(x => x.SignatureAnchor).HasMaxLength(80);
        b.Property(x => x.ActedByName).HasMaxLength(200);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.Ignore(x => x.ReturnToOrders);
        b.Ignore(x => x.RequiresSignature);
        b.Ignore(x => x.IsSupplierStep);
        b.Property(x => x.RowVersion).IsRowVersion();

        // Inbox queries: active steps by assignee.
        b.HasIndex(x => new { x.Status, x.RoleCode });
        b.HasIndex(x => new { x.Status, x.AssigneeUserId });
        b.HasIndex(x => new { x.Status, x.DueAt });
        b.HasIndex(x => x.ActedBy);
    }
}

internal sealed class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> b)
    {
        b.ToTable("workflow_events");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StepName).HasMaxLength(200);
        b.Property(x => x.ActorName).HasMaxLength(200);
        b.Property(x => x.Note).HasMaxLength(2000);
        b.HasIndex(x => new { x.InstanceId, x.Sequence }).IsUnique();
    }
}

internal sealed class SignatureConfiguration : IEntityTypeConfiguration<Signature>
{
    public void Configure(EntityTypeBuilder<Signature> b)
    {
        b.ToTable("signatures");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SignerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.SignerTitle).HasMaxLength(200);
        b.Property(x => x.Caption).HasMaxLength(200);
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.FieldName).HasMaxLength(100).IsRequired();
        b.Property(x => x.CertificateSerial).HasMaxLength(100);
        b.Property(x => x.CertificateSubject).HasMaxLength(500);
        b.Property(x => x.CertificateIssuer).HasMaxLength(500);
        b.Property(x => x.HashBefore).HasMaxLength(64).IsRequired();
        b.Property(x => x.HashAfter).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.DocumentVersionId);
        b.HasIndex(x => new { x.SignerUserId, x.SignedAt });
    }
}

internal sealed class SigningRequestConfiguration : IEntityTypeConfiguration<SigningRequest>
{
    public void Configure(EntityTypeBuilder<SigningRequest> b)
    {
        b.ToTable("signing_requests");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.FieldName).HasMaxLength(100).IsRequired();
        b.Property(x => x.PreparedPdfKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.BasePdfSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.DocumentDigest).HasMaxLength(64).IsRequired();
        b.Property(x => x.ProviderMessage).HasMaxLength(500);
        b.Property(x => x.Error).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.StepInstanceId, x.Status });
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasMaxLength(60).IsRequired();
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Body).HasMaxLength(2000);
        b.Property(x => x.Link).HasMaxLength(500);
        b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
    }
}

internal sealed class SigningCertificateConfiguration : IEntityTypeConfiguration<SigningCertificate>
{
    public void Configure(EntityTypeBuilder<SigningCertificate> b)
    {
        b.ToTable("signing_certificates");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        b.Property(x => x.SerialNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.Issuer).HasMaxLength(500).IsRequired();
        b.Property(x => x.ProtectedPfx).IsRequired();
        b.HasIndex(x => new { x.UserId, x.Provider });
    }
}
