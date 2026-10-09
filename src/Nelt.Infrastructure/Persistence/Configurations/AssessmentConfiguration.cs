using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelt.Domain.Entities;

namespace Nelt.Infrastructure.Persistence.Configurations;

internal sealed class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> builder)
    {
        builder.Property(q => q.Title).HasMaxLength(160).IsRequired();
        builder.Property(q => q.Instructions).HasMaxLength(4000);
        builder.HasOne(q => q.Course).WithMany(c => c.Quizzes).HasForeignKey(q => q.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Lesson).WithMany().HasForeignKey(q => q.LessonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(q => new { q.CourseId, q.Kind });
    }
}

internal sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.Property(q => q.Prompt).HasMaxLength(2000).IsRequired();
        builder.HasOne(q => q.Quiz).WithMany(q => q.Questions).HasForeignKey(q => q.QuizId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QuestionOptionConfiguration : IEntityTypeConfiguration<QuestionOption>
{
    public void Configure(EntityTypeBuilder<QuestionOption> builder)
    {
        builder.ToTable("QuestionOptions");
        builder.Property(o => o.Text).HasMaxLength(500).IsRequired();
        builder.HasOne(o => o.Question).WithMany(q => q.Options).HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> builder)
    {
        builder.Property(a => a.EarnedPoints).HasPrecision(7, 2);
        builder.Property(a => a.TotalPoints).HasPrecision(7, 2);
        builder.Property(a => a.ScorePercent).HasPrecision(5, 2);
        builder.Property(a => a.Note).HasMaxLength(300);
        builder.HasOne(a => a.Quiz).WithMany(q => q.Attempts).HasForeignKey(a => a.QuizId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Enrollment).WithMany(e => e.QuizAttempts).HasForeignKey(a => a.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.EnrollmentId, a.QuizId });
    }
}

internal sealed class AttemptAnswerConfiguration : IEntityTypeConfiguration<AttemptAnswer>
{
    public void Configure(EntityTypeBuilder<AttemptAnswer> builder)
    {
        builder.Property(a => a.SelectedOptionIds).HasMaxLength(400).IsRequired();
        builder.Property(a => a.PointsAwarded).HasPrecision(7, 2);
        builder.HasOne(a => a.Attempt).WithMany(a => a.Answers).HasForeignKey(a => a.AttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Question>().WithMany().HasForeignKey(a => a.QuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.Property(a => a.Title).HasMaxLength(160).IsRequired();
        builder.Property(a => a.Instructions).HasMaxLength(8000).IsRequired();
        builder.Property(a => a.AttachmentKey).HasMaxLength(200);
        builder.Property(a => a.AttachmentName).HasMaxLength(200);
        builder.HasOne(a => a.Course).WithMany(c => c.Assignments).HasForeignKey(a => a.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Lesson).WithMany().HasForeignKey(a => a.LessonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.CourseId, a.DueAt });
    }
}

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.Property(s => s.FileKey).HasMaxLength(200);
        builder.Property(s => s.FileName).HasMaxLength(200);
        builder.Property(s => s.Score).HasPrecision(7, 2);
        builder.Property(s => s.Feedback).HasMaxLength(4000);
        builder.HasIndex(s => new { s.AssignmentId, s.EnrollmentId }).IsUnique();
        builder.HasOne(s => s.Assignment).WithMany(a => a.Submissions).HasForeignKey(s => s.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Enrollment).WithMany(e => e.Submissions).HasForeignKey(s => s.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
