using BlueContract.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueContract.Infrastructure.Persistence.Configurations;

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings");
        builder.Property(x => x.Key).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ValueType).HasMaxLength(20);
        builder.HasIndex(x => x.Key).IsUnique();
    }
}

internal sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("email_templates");
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.BodyHtml).HasMaxLength(20000).IsRequired();
        builder.HasIndex(x => x.EventType).IsUnique().HasFilter("is_deleted = false");
    }
}
