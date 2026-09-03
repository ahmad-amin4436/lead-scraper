using LeadMine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadMine.Infrastructure.Persistence.Configurations;

public class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("EmailTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(128).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(512);
        builder.Property(t => t.Subject).HasMaxLength(256).IsRequired();
        builder.Property(t => t.BodyHtml).IsRequired();

        builder.HasIndex(t => t.Name).IsUnique();
        builder.HasIndex(t => t.IsActive);

        builder.HasOne(t => t.Signature)
            .WithMany()
            .HasForeignKey(t => t.SignatureId)
            // Restrict, not Cascade: removing a signature must not silently
            // delete the presets that reference it.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmailSignatureConfiguration : IEntityTypeConfiguration<EmailSignature>
{
    public void Configure(EntityTypeBuilder<EmailSignature> builder)
    {
        builder.ToTable("EmailSignatures");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(128).IsRequired();
        builder.Property(s => s.BodyHtml).IsRequired();

        builder.HasIndex(s => s.OwnerUserId);
        builder.HasIndex(s => s.IsDefault);
    }
}

public class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("EmailLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.SenderEmail).HasMaxLength(256).IsRequired();
        builder.Property(l => l.SenderName).HasMaxLength(256);
        builder.Property(l => l.ToEmail).HasMaxLength(256).IsRequired();
        builder.Property(l => l.ToName).HasMaxLength(256);
        builder.Property(l => l.Cc).HasMaxLength(512);
        builder.Property(l => l.Bcc).HasMaxLength(512);
        builder.Property(l => l.Subject).HasMaxLength(512).IsRequired();
        builder.Property(l => l.TemplateName).HasMaxLength(128);
        builder.Property(l => l.Error).HasMaxLength(2000);
        builder.Property(l => l.MessageId).HasMaxLength(256);
        builder.Property(l => l.Status).HasConversion<int>();

        builder.Property(l => l.BounceType).HasConversion<int?>();
        builder.Property(l => l.BounceReason).HasMaxLength(2000);
        builder.Property(l => l.BounceDsnCode).HasMaxLength(32);

        // The admin screen filters by sender and date range, so index the pair.
        builder.HasIndex(l => l.CreatedAt);
        builder.HasIndex(l => new { l.SenderUserId, l.CreatedAt });
        builder.HasIndex(l => l.Status);
        builder.HasIndex(l => l.ToEmail);
        builder.HasIndex(l => l.BusinessId);

        // EmailBounceProcessorService's exact-match lookup: "which send did
        // this DSN's attached original message belong to". Filtered since
        // most rows never carry one worth indexing (dry runs, failed sends).
        builder.HasIndex(l => l.MessageId).HasFilter("[MessageId] IS NOT NULL");

        builder.HasOne(l => l.Template)
            .WithMany(t => t.Sends)
            .HasForeignKey(l => l.TemplateId)
            // Keep the audit row if the preset is ever hard-deleted; the
            // denormalised TemplateName preserves what was actually sent.
            .OnDelete(DeleteBehavior.SetNull);
    }
}
