using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelt.Domain.Entities;

namespace Nelt.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.PreferredLanguage).HasMaxLength(5).IsRequired();
        builder.Property(u => u.BiometricId).HasMaxLength(32);
        builder.HasIndex(u => u.BiometricId).IsUnique().HasFilter("[BiometricId] IS NOT NULL");
        builder.HasIndex(u => u.FullName);
    }
}
