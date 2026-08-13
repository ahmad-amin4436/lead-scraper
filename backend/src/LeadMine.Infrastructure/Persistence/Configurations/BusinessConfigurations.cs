using LeadMine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadMine.Infrastructure.Persistence.Configurations;

public class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ToTable("Businesses");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(256).IsRequired();
        builder.Property(b => b.Category).HasMaxLength(128);
        builder.Property(b => b.Country).HasMaxLength(128);
        builder.Property(b => b.State).HasMaxLength(128);
        builder.Property(b => b.City).HasMaxLength(128);
        builder.Property(b => b.Address).HasMaxLength(512);
        builder.Property(b => b.Phone).HasMaxLength(64);
        builder.Property(b => b.Website).HasMaxLength(512);
        builder.Property(b => b.Email).HasMaxLength(256);
        builder.Property(b => b.WhatsApp).HasMaxLength(512);
        builder.Property(b => b.Facebook).HasMaxLength(512);
        builder.Property(b => b.Instagram).HasMaxLength(512);
        builder.Property(b => b.LinkedIn).HasMaxLength(512);
        builder.Property(b => b.MapsUrl).HasMaxLength(1024);
        builder.Property(b => b.Notes).HasMaxLength(2000);

        builder.Property(b => b.PostalCode).HasMaxLength(32);
        builder.Property(b => b.PlaceId).HasMaxLength(256);
        builder.Property(b => b.Industry).HasMaxLength(256);
        builder.Property(b => b.CompanyDescription).HasMaxLength(4000);

        builder.Property(b => b.DedupeWebsiteKey).HasMaxLength(256);
        builder.Property(b => b.DedupePhoneKey).HasMaxLength(32);
        builder.Property(b => b.DedupeNameKey).HasMaxLength(384);

        // Enums persist as int; the DTO layer exposes the names.
        builder.Property(b => b.Source).HasConversion<int>();
        builder.Property(b => b.Status).HasConversion<int>();
        builder.Property(b => b.EmailStatus).HasConversion<int>();
        builder.Property(b => b.WhatsAppStatus).HasConversion<int>();

        // No HasPrecision here. Precision/scale are decimal concepts; applied to
        // a double EF emits `float(3)`, which SQL Server stores as 4-byte `real`
        // — so a rating of 4.9 came back as 4.900000095367432 and would show
        // that way in an export. Plain `float` round-trips it exactly.

        // Indexes chosen to match the list filters the UI actually issues.
        builder.HasIndex(b => b.Name);
        builder.HasIndex(b => b.Category);
        builder.HasIndex(b => new { b.Country, b.City });
        builder.HasIndex(b => b.Status);
        builder.HasIndex(b => b.EmailStatus);
        builder.HasIndex(b => b.WhatsAppStatus);
        builder.HasIndex(b => b.CreatedAt);

        // Every lead query is scoped by owner, so this is the hottest index in
        // the table; the composite matches the default "my leads, newest first".
        builder.HasIndex(b => b.OwnerUserId);
        builder.HasIndex(b => new { b.OwnerUserId, b.CreatedAt });

        // Duplicate detection: filtered so many NULLs don't bloat the index.
        builder.HasIndex(b => b.DedupeWebsiteKey).HasFilter("[DedupeWebsiteKey] IS NOT NULL");
        builder.HasIndex(b => b.DedupePhoneKey).HasFilter("[DedupePhoneKey] IS NOT NULL");
        builder.HasIndex(b => b.DedupeNameKey).HasFilter("[DedupeNameKey] IS NOT NULL");

        builder.HasOne(b => b.SearchJob)
            .WithMany(j => j.Businesses)
            .HasForeignKey(b => b.SearchJobId)
            // Keep the leads if their originating run is purged.
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.CompanyName).HasMaxLength(256);
        builder.Property(p => p.FullName).HasMaxLength(256).IsRequired();
        builder.Property(p => p.JobTitle).HasMaxLength(256);
        builder.Property(p => p.Headline).HasMaxLength(512);
        builder.Property(p => p.LinkedInUrl).HasMaxLength(512);
        builder.Property(p => p.Location).HasMaxLength(256);
        builder.Property(p => p.Email).HasMaxLength(256);
        builder.Property(p => p.Phone).HasMaxLength(64);
        builder.Property(p => p.DecisionMakerRole).HasMaxLength(256);

        builder.HasIndex(p => p.BusinessId);
        builder.HasIndex(p => p.OwnerUserId);
        builder.HasIndex(p => new { p.OwnerUserId, p.CreatedAt });
        builder.HasIndex(p => p.IsDecisionMaker);
        builder.HasIndex(p => p.LinkedInUrl);

        builder.HasOne(p => p.Business)
            .WithMany(b => b.People)
            .HasForeignKey(p => p.BusinessId)
            // Keep the person record if their associated business is purged —
            // the LinkedIn data still stands on its own.
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class SearchJobConfiguration : IEntityTypeConfiguration<SearchJob>
{
    public void Configure(EntityTypeBuilder<SearchJob> builder)
    {
        builder.ToTable("SearchJobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Status).HasConversion<int>();
        builder.Property(j => j.RequestJson).IsRequired();
        builder.Property(j => j.CurrentTask).HasMaxLength(256);
        builder.Property(j => j.Error).HasMaxLength(2000);

        builder.HasIndex(j => j.Status);
        builder.HasIndex(j => j.StartedAt);
        // Supports the "find runs whose worker died" sweep.
        builder.HasIndex(j => new { j.Status, j.HeartbeatAt });
    }
}

public class ExportRecordConfiguration : IEntityTypeConfiguration<ExportRecord>
{
    public void Configure(EntityTypeBuilder<ExportRecord> builder)
    {
        builder.ToTable("Exports");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FileName).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Format).HasMaxLength(16).IsRequired();
        builder.Property(e => e.Template).HasMaxLength(32).IsRequired();
        builder.Property(e => e.StorageKey).HasMaxLength(512);
        builder.Property(e => e.FiltersJson).IsRequired();

        builder.HasIndex(e => e.CreatedAt);
    }
}

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("ActivityLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Level).HasConversion<int>();
        builder.Property(l => l.Event).HasMaxLength(64).IsRequired();
        builder.Property(l => l.Message).HasMaxLength(2000).IsRequired();
        builder.Property(l => l.ContextJson).IsRequired();

        builder.HasIndex(l => l.Timestamp);
        builder.HasIndex(l => l.Level);
        builder.HasIndex(l => l.Event);
        builder.HasIndex(l => l.SearchJobId);
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Key).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(2000);
        builder.Property(s => s.Description).HasMaxLength(512);

        builder.HasIndex(s => s.Key).IsUnique();
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.Action).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(128);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);
        builder.Property(a => a.DetailsJson).IsRequired();

        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.Action);
    }
}

public class LinkedInAccountSessionConfiguration : IEntityTypeConfiguration<LinkedInAccountSession>
{
    public void Configure(EntityTypeBuilder<LinkedInAccountSession> builder)
    {
        builder.ToTable("LinkedInAccountSessions");
        builder.HasKey(s => s.Id);

        // No length cap: a real storageState.json runs several KB and grows
        // with the account's cookie count, not something to bound tightly.
        builder.Property(s => s.StorageStateJson).IsRequired();
        builder.Property(s => s.RestrictedReason).HasMaxLength(512);

        // One session per user — every lookup and the upload upsert both go
        // through this, so it is the hot path for the whole feature.
        builder.HasIndex(s => s.UserId).IsUnique();
    }
}
