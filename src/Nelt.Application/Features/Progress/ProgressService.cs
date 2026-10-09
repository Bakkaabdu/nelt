using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Progress;

public sealed record RosterRow(
    int EnrollmentId,
    string StudentName,
    string Email,
    StudyMode Mode,
    EnrollmentStatus Status,
    string? CurrentLesson,
    DateTime? LastActivityAt,
    PerformanceSnapshot Performance,
    CompletionEvaluation Evaluation);

public sealed record CourseRoster(CourseHeader Course, CompletionPolicy Policy, int LessonCount, IReadOnlyList<RosterRow> Rows);

public sealed record LessonProgressItem(int LessonId, string Title, DateTime? CompletedAt, bool IsCurrent);

public sealed record AttemptItem(string QuizTitle, QuizKind Kind, AttemptSource Source, DateTime? SubmittedAt, decimal? Score);

public sealed record SubmissionItem(string Title, DateTime? DueAt, DateTime? SubmittedAt, bool IsLate, decimal? Score, int MaxScore);

public sealed record AttendanceItem(DateTime StartsAt, string? Topic, AttendanceStatus Status, AttendanceSource Source, DateTime? CheckInAt);

public sealed record StudentProgress(
    CourseHeader Course,
    CompletionPolicy Policy,
    int EnrollmentId,
    string StudentName,
    string Email,
    string? Phone,
    string? BiometricId,
    StudyMode Mode,
    EnrollmentStatus Status,
    DateTime? ActivatedAt,
    DateTime? LastActivityAt,
    IReadOnlyList<LessonProgressItem> Lessons,
    IReadOnlyList<AttemptItem> Attempts,
    IReadOnlyList<SubmissionItem> Submissions,
    IReadOnlyList<AttendanceItem> Attendance,
    PerformanceSnapshot Performance,
    CompletionEvaluation Evaluation,
    CertificateStatus? CertificateStatus);

public interface IProgressService
{
    Task<Result<CourseRoster>> RosterAsync(int courseId, CancellationToken ct = default);
    Task<Result<StudentProgress>> StudentAsync(int courseId, int enrollmentId, CancellationToken ct = default);
}

/// <summary>Lets instructors see where every student is in a course and how they are performing.</summary>
internal sealed class ProgressService(IAppDbContext db, ICourseAccess access, PerformanceCalculator performance) : IProgressService
{
    public async Task<Result<CourseRoster>> RosterAsync(int courseId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var policy = await db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => c.Policy).FirstAsync(ct);
        var lessonCount = await db.Lessons.CountAsync(l => l.CourseId == courseId && l.IsPublished, ct);

        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed))
            .OrderBy(e => e.Student!.FullName)
            .Select(e => new
            {
                e.Id, e.Student!.FullName, Email = e.Student.Email ?? string.Empty, e.Mode, e.Status, e.LastActivityAt,
                Current = db.Lessons.Where(l => l.Id == e.LastLessonId).Select(l => l.Title).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var snapshots = await performance.ForCourseAsync(courseId, null, ct);
        var rows = enrollments.Select(e =>
        {
            var snapshot = snapshots.GetValueOrDefault(e.Id) ?? new PerformanceSnapshot { Mode = e.Mode };
            return new RosterRow(e.Id, e.FullName, e.Email, e.Mode, e.Status, e.Current, e.LastActivityAt, snapshot,
                CompletionEvaluator.Evaluate(policy, snapshot));
        }).ToList();

        return new CourseRoster(course.Value, policy, lessonCount, rows);
    }

    public async Task<Result<StudentProgress>> StudentAsync(int courseId, int enrollmentId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var e = await db.Enrollments.AsNoTracking().Include(x => x.Student).Include(x => x.Certificate)
            .FirstOrDefaultAsync(x => x.Id == enrollmentId && x.CourseId == courseId, ct);
        if (e is null)
        {
            return Error.NotFound();
        }

        var policy = await db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => c.Policy).FirstAsync(ct);

        var lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == courseId && l.IsPublished)
            .OrderBy(l => l.SortOrder)
            .Select(l => new LessonProgressItem(l.Id, l.Title,
                db.LessonProgress.Where(p => p.LessonId == l.Id && p.EnrollmentId == enrollmentId).Select(p => (DateTime?)p.CompletedAt).FirstOrDefault(),
                l.Id == e.LastLessonId))
            .ToListAsync(ct);

        var attempts = await db.QuizAttempts.AsNoTracking().Where(a => a.EnrollmentId == enrollmentId)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .Select(a => new AttemptItem(a.Quiz!.Title, a.Quiz.Kind, a.Source, a.SubmittedAt, a.ScorePercent))
            .ToListAsync(ct);

        var submissions = await db.Assignments.AsNoTracking().Where(a => a.CourseId == courseId && a.IsPublished)
            .OrderBy(a => a.DueAt)
            .Select(a => new
            {
                a.Title, a.DueAt, a.MaxScore,
                Sub = a.Submissions.Where(s => s.EnrollmentId == enrollmentId).Select(s => new { s.SubmittedAt, s.IsLate, s.Score }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var attendance = await db.AttendanceRecords.AsNoTracking().Where(r => r.EnrollmentId == enrollmentId)
            .OrderByDescending(r => r.Session!.StartsAt)
            .Select(r => new AttendanceItem(r.Session!.StartsAt, r.Session.Topic, r.Status, r.Source, r.CheckInAt))
            .ToListAsync(ct);

        var snapshot = await performance.ForEnrollmentAsync(courseId, enrollmentId, ct);

        return new StudentProgress(course.Value, policy, e.Id, e.Student!.FullName, e.Student.Email ?? string.Empty, e.Student.PhoneNumber,
            e.Student.BiometricId, e.Mode, e.Status, e.ActivatedAt, e.LastActivityAt, lessons, attempts,
            submissions.Select(s => new SubmissionItem(s.Title, s.DueAt, s.Sub?.SubmittedAt, s.Sub?.IsLate ?? false, s.Sub?.Score, s.MaxScore)).ToList(),
            attendance, snapshot, CompletionEvaluator.Evaluate(policy, snapshot), e.Certificate?.Status);
    }
}
