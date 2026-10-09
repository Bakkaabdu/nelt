using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nelt.Domain.Entities;

namespace Nelt.Application.Abstractions;

/// <summary>Unit of work over the relational store. Implemented by the EF Core context in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<IdentityRole<Guid>> Roles { get; }
    DbSet<IdentityUserRole<Guid>> UserRoles { get; }
    DbSet<PlatformSettings> PlatformSettings { get; }
    DbSet<Level> Levels { get; }
    DbSet<Course> Courses { get; }
    DbSet<Lesson> Lessons { get; }
    DbSet<LessonProgress> LessonProgress { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<Quiz> Quizzes { get; }
    DbSet<Question> Questions { get; }
    DbSet<QuestionOption> QuestionOptions { get; }
    DbSet<QuizAttempt> QuizAttempts { get; }
    DbSet<AttemptAnswer> AttemptAnswers { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<Submission> Submissions { get; }
    DbSet<LearningMaterial> Materials { get; }
    DbSet<CourseEvent> Events { get; }
    DbSet<ClassSession> ClassSessions { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<BiometricDevice> BiometricDevices { get; }
    DbSet<BiometricPunch> BiometricPunches { get; }
    DbSet<CertificateRequest> CertificateRequests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs the operation in a transaction wrapped by the provider's retrying execution strategy.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);

    /// <summary>True when the exception is a unique-constraint violation (used for idempotent inserts under concurrency).</summary>
    bool IsUniqueViolation(Exception exception);

    /// <summary>Detaches all tracked entities (used after a failed save before retrying the next item).</summary>
    void ClearChangeTracker();
}
