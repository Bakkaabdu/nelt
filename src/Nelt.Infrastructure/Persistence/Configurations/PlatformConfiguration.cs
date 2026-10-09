using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelt.Domain.Entities;

namespace Nelt.Infrastructure.Persistence.Configurations;

internal sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("PlatformSettings");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.SiteName).HasMaxLength(60).IsRequired();
        builder.Localized(s => s.Tagline, 160)
            .Localized(s => s.HeroTitle, 160)
            .Localized(s => s.HeroSubtitle, 400)
            .Localized(s => s.AboutTitle, 160)
            .Localized(s => s.AboutBody, 4000)
            .Localized(s => s.PaymentInstructions, 1000)
            .Localized(s => s.Address, 300);
        builder.Property(s => s.ContactEmail).HasMaxLength(160);
        builder.Property(s => s.ContactPhone).HasMaxLength(40);
        builder.Property(s => s.WhatsAppNumber).HasMaxLength(40);
        builder.Property(s => s.FacebookUrl).HasMaxLength(300);
        builder.Property(s => s.InstagramUrl).HasMaxLength(300);
        builder.Property(s => s.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
    }
}

internal sealed class LevelConfiguration : IEntityTypeConfiguration<Level>
{
    public void Configure(EntityTypeBuilder<Level> builder)
    {
        builder.Property(l => l.Code).HasMaxLength(16).IsRequired();
        builder.Localized(l => l.Name, 80).Localized(l => l.Description, 600);
        builder.HasIndex(l => new { l.Language, l.Code }).IsUnique();
        builder.HasIndex(l => new { l.Language, l.Rank }).IsUnique();
    }
}

internal sealed class CourseEventConfiguration : IEntityTypeConfiguration<CourseEvent>
{
    public void Configure(EntityTypeBuilder<CourseEvent> builder)
    {
        builder.ToTable("Events");
        builder.Localized(e => e.Title, 160).Localized(e => e.Description, 4000);
        builder.Property(e => e.Location).HasMaxLength(200);
        builder.HasOne(e => e.Course).WithMany().HasForeignKey(e => e.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.IsPublished, e.StartsAt });
    }
}

internal sealed class CertificateRequestConfiguration : IEntityTypeConfiguration<CertificateRequest>
{
    public void Configure(EntityTypeBuilder<CertificateRequest> builder)
    {
        builder.Property(r => r.FinalScore).HasPrecision(5, 2);
        builder.Property(r => r.ReviewNote).HasMaxLength(500);
        builder.Property(r => r.SerialNumber).HasMaxLength(40);
        builder.HasIndex(r => r.SerialNumber).IsUnique().HasFilter("[SerialNumber] IS NOT NULL");
        builder.HasIndex(r => r.Status);
        builder.HasOne(r => r.Enrollment).WithOne(e => e.Certificate)
            .HasForeignKey<CertificateRequest>(r => r.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
