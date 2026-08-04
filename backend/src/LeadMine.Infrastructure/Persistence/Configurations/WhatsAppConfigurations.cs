using LeadMine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadMine.Infrastructure.Persistence.Configurations;

public class WhatsAppTemplateConfiguration : IEntityTypeConfiguration<WhatsAppTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplate> builder)
    {
        builder.ToTable("WhatsAppTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(128).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(512);
        builder.Property(t => t.Message).HasMaxLength(4096).IsRequired();

        builder.HasIndex(t => t.Name).IsUnique();
        builder.HasIndex(t => t.IsActive);
    }
}

public class WhatsAppContactLogConfiguration : IEntityTypeConfiguration<WhatsAppContactLog>
{
    public void Configure(EntityTypeBuilder<WhatsAppContactLog> builder)
    {
        builder.ToTable("WhatsAppContactLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.SenderEmail).HasMaxLength(256).IsRequired();
        builder.Property(l => l.ToName).HasMaxLength(256);
        builder.Property(l => l.ToPhone).HasMaxLength(64);
        builder.Property(l => l.TemplateName).HasMaxLength(128);
        builder.Property(l => l.Message).HasMaxLength(4096).IsRequired();

        builder.HasIndex(l => l.CreatedAt);
        builder.HasIndex(l => new { l.SenderUserId, l.CreatedAt });
        builder.HasIndex(l => l.BusinessId);

        builder.HasOne(l => l.Template)
            .WithMany(t => t.Contacts)
            .HasForeignKey(l => l.TemplateId)
            // Keep the audit row if the preset is ever hard-deleted; the
            // denormalised TemplateName preserves what was actually sent.
            .OnDelete(DeleteBehavior.SetNull);
    }
}
