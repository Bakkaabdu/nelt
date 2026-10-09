using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelt.Domain.Entities;

namespace Nelt.Infrastructure.Persistence.Configurations;

// Foreign keys are RESTRICT unless an entity is a pure part of its parent (questions, options, answers).
// This avoids SQL Server "multiple cascade paths" and makes destructive operations explicit in the services.
internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.Property(c => c.Slug).HasMaxLength(80).IsRequired();
        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasIndex(c => new { c.IsPublished, c.SortOrder });
        builder.Localized(c => c.Title, 160)
            .Localized(c => c.Summary, 400)
            .Localized(c => c.Description, null)
            .Localized(c => c.ScheduleNote, 200);
        builder.Property(c => c.CoverImageKey).HasMaxLength(200);
        builder.ComplexProperty(c => c.Policy, p => p.IsRequired());

        builder.HasOne(c => c.Level).WithMany(l => l.Courses).HasForeignKey(c => c.LevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Instructor).WithMany().HasForeignKey(c => c.InstructorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.Property(l => l.Title).HasMaxLength(160).IsRequired();
        builder.Property(l => l.Summary).HasMaxLength(2000);
        builder.Property(l => l.VideoKey).HasMaxLength(200);
        builder.Property(l => l.VideoContentType).HasMaxLength(100);
        builder.Property(l => l.VideoUrl).HasMaxLength(400);
        builder.HasOne(l => l.Course).WithMany(c => c.Lessons).HasForeignKey(l => l.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.CourseId, l.SortOrder });
    }
}

internal sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> builder)
    {
        builder.HasIndex(p => new { p.EnrollmentId, p.LessonId }).IsUnique();
        builder.HasOne(p => p.Enrollment).WithMany(e => e.LessonProgress).HasForeignKey(p => p.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Lesson).WithMany().HasForeignKey(p => p.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.HasIndex(e => new { e.StudentId, e.CourseId }).IsUnique();
        builder.HasIndex(e => new { e.CourseId, e.Status });
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
        builder.Property(e => e.PaymentReference).HasMaxLength(120);
        builder.HasOne(e => e.Student).WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Course).WithMany(c => c.Enrollments).HasForeignKey(e => e.CourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LearningMaterialConfiguration : IEntityTypeConfiguration<LearningMaterial>
{
    public void Configure(EntityTypeBuilder<LearningMaterial> builder)
    {
        builder.ToTable("Materials");
        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.Property(m => m.FileKey).HasMaxLength(200).IsRequired();
        builder.Property(m => m.FileName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(100).IsRequired();
        builder.HasOne(m => m.Level).WithMany(l => l.Materials).HasForeignKey(m => m.LevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Course).WithMany().HasForeignKey(m => m.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.LevelId, m.Type, m.SortOrder });
    }
}
