using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelt.Domain.Entities;

namespace Nelt.Infrastructure.Persistence.Configurations;

internal sealed class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        builder.Property(s => s.Topic).HasMaxLength(200);
        builder.Property(s => s.Room).HasMaxLength(80);
        builder.HasOne(s => s.Course).WithMany(c => c.Sessions).HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.CourseId, s.StartsAt });
        builder.HasIndex(s => new { s.IsFinalized, s.EndsAt });
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.Property(r => r.Note).HasMaxLength(200);
        builder.HasIndex(r => new { r.SessionId, r.EnrollmentId }).IsUnique();
        builder.HasIndex(r => r.EnrollmentId);
        builder.HasOne(r => r.Session).WithMany(s => s.Records).HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Enrollment).WithMany(e => e.Attendance).HasForeignKey(r => r.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BiometricDeviceConfiguration : IEntityTypeConfiguration<BiometricDevice>
{
    public void Configure(EntityTypeBuilder<BiometricDevice> builder)
    {
        builder.Property(d => d.Name).HasMaxLength(80).IsRequired();
        builder.Property(d => d.SerialNumber).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ApiKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Location).HasMaxLength(120);
        builder.HasIndex(d => d.SerialNumber).IsUnique();
        builder.HasIndex(d => d.ApiKeyHash);
    }
}

internal sealed class BiometricPunchConfiguration : IEntityTypeConfiguration<BiometricPunch>
{
    public void Configure(EntityTypeBuilder<BiometricPunch> builder)
    {
        builder.Property(p => p.DeviceUserId).HasMaxLength(32).IsRequired();
        builder.HasIndex(p => new { p.DeviceId, p.DeviceUserId, p.PunchedAt }).IsUnique();
        builder.HasIndex(p => p.PunchedAt);
        builder.HasOne(p => p.Device).WithMany().HasForeignKey(p => p.DeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.AttendanceRecord).WithMany().HasForeignKey(p => p.AttendanceRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
