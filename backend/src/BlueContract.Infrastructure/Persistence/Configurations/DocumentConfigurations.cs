using BlueContract.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("documents");
        b.Property(x => x.Title).HasMaxLength(500).IsRequired();
        b.Property(x => x.OwnerEntityType).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.OwnerEntityType, x.OwnerId });
        b.HasIndex(x => x.ContractId);
        b.HasIndex(x => x.SupplierId);
        b.HasIndex(x => x.BidDecisionId);
        b.Ignore(x => x.CurrentVersion);
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> b)
    {
        b.ToTable("document_versions");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.CreatedByName).HasMaxLength(200);
        b.Property(x => x.DocxFileKey).HasMaxLength(500);
        b.Property(x => x.PdfFileKey).HasMaxLength(500);
        b.Property(x => x.DocxSha256).HasMaxLength(64);
        b.Property(x => x.PdfSha256).HasMaxLength(64);
        b.Ignore(x => x.IsLocked);
        b.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
    }
}

internal sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> b)
    {
        b.ToTable("document_templates");
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Ignore(x => x.ActiveVersion);
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class DocumentTemplateVersionConfiguration : IEntityTypeConfiguration<DocumentTemplateVersion>
{
    public void Configure(EntityTypeBuilder<DocumentTemplateVersion> b)
    {
        b.ToTable("document_template_versions");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FileKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.Placeholders).IsRequired();
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.TemplateId, x.VersionNumber }).IsUnique();
    }
}

internal sealed class DocumentEditSessionConfiguration : IEntityTypeConfiguration<DocumentEditSession>
{
    public void Configure(EntityTypeBuilder<DocumentEditSession> b)
    {
        b.ToTable("document_edit_sessions");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Mode).HasMaxLength(20).IsRequired();
        b.Ignore(x => x.IsOpen);
        b.HasIndex(x => x.DocumentId);
    }
}
