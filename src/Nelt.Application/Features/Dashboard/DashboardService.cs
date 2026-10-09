using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Dashboard;

public sealed record AdminDashboard(
    int Students,
    int Instructors,
    int PublishedCourses,
    int PendingEnrollments,
    int ActiveEnrollments,
    int PendingCertificates,
    int ActiveDevices,
    int SessionsToday,
    decimal RevenueThisMonth,
    string Currency,
    IReadOnlyList<RecentEnrollment> RecentEnrollments,
    IReadOnlyList<DeviceStatus> Devices);

public sealed record RecentEnrollment(int Id, string StudentName, LocalizedText CourseTitle, string LevelCode, EnrollmentStatus Status, StudyMode Mode, DateTime CreatedAt);

public sealed record DeviceStatus(int Id, string Name, bool IsActive, DateTime? LastSeenAt);

public sealed record TeachCourseRow(
    int Id,
    LocalizedText Title,
    string LevelCode,
    TargetLanguage Language,
    DeliveryMode DeliveryMode,
    bool IsPublished,
    int ActiveStudents,
    int Lessons,
    int SubmissionsToGrade,
    int PendingCertificates,
    DateTime? NextSession);

public sealed record CourseOverview(
    CourseHeader Course,
    int ActiveStudents,
    int InPersonStudents,
    int Lessons,
    int Quizzes,
    int Assignments,
    int SubmissionsToGrade,
    int PendingCertificates,
    int UpcomingSessions,
    IReadOnlyList<(DateTime StartsAt, DateTime EndsAt, string? Topic, int Id)> NextSessions);

public interface IDashboardService
{
    Task<AdminDashboard> AdminAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TeachCourseRow>> TeachingAsync(CancellationToken ct = default);
    Task<Result<CourseOverview>> CourseOverviewAsync(int courseId, CancellationToken ct = default);
}

internal sealed class DashboardService(IAppDbContext db, ICourseAccess access, IPlatformTime time, Settings.IPlatformSettingsService settings) : IDashboardService
{
    public async Task<AdminDashboard> AdminAsync(CancellationToken ct = default)
    {
        var now = time.UtcNow;
        var localToday = time.LocalToday;
        var dayStart = time.ToUtc(localToday.ToDateTime(TimeOnly.MinValue));
        var dayEnd = dayStart.AddDays(1);
        var monthStart = time.ToUtc(new DateOnly(localToday.Year, localToday.Month, 1).ToDateTime(TimeOnly.MinValue));

        var students = await db.UserIdsInRoles(Roles.Student).CountAsync(ct);
        var instructors = await db.UserIdsInRoles(Roles.Instructor).CountAsync(ct);
        var courses = await db.Courses.CountAsync(c => c.IsPublished, ct);
        var pending = await db.Enrollments.CountAsync(e => e.Status == EnrollmentStatus.Pending, ct);
        var active = await db.Enrollments.CountAsync(e => e.Status == EnrollmentStatus.Active, ct);
        var certificates = await db.CertificateRequests.CountAsync(r => r.Status == CertificateStatus.Pending, ct);
        var devices = await db.BiometricDevices.CountAsync(d => d.IsActive, ct);
        var sessionsToday = await db.ClassSessions.CountAsync(s => s.StartsAt >= dayStart && s.StartsAt < dayEnd, ct);
        var revenue = await db.Enrollments.Where(e => e.ActivatedAt >= monthStart && e.ActivatedAt <= now).SumAsync(e => e.AmountPaid ?? 0, ct);

        var recent = await db.Enrollments.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(8)
            .Select(e => new RecentEnrollment(e.Id, e.Student!.FullName, e.Course!.Title, e.Course.Level!.Code, e.Status, e.Mode, e.CreatedAt))
            .ToListAsync(ct);

        var deviceStatus = await db.BiometricDevices.AsNoTracking().OrderBy(d => d.Name).Take(6)
            .Select(d => new DeviceStatus(d.Id, d.Name, d.IsActive, d.LastSeenAt)).ToListAsync(ct);

        var site = await settings.GetAsync(ct);
        return new AdminDashboard(students, instructors, courses, pending, active, certificates, devices, sessionsToday, revenue, site.Currency, recent, deviceStatus);
    }

    public async Task<IReadOnlyList<TeachCourseRow>> TeachingAsync(CancellationToken ct = default)
    {
        var now = time.UtcNow;
        return await access.ManageableCourses().AsNoTracking()
            .OrderBy(c => c.Level!.Language).ThenBy(c => c.Level!.Rank).ThenBy(c => c.SortOrder)
            .Select(c => new TeachCourseRow(
                c.Id, c.Title, c.Level!.Code, c.Level.Language, c.DeliveryMode, c.IsPublished,
                c.Enrollments.Count(e => e.Status == EnrollmentStatus.Active),
                c.Lessons.Count,
                db.Submissions.Count(s => s.Assignment!.CourseId == c.Id && s.Score == null),
                db.CertificateRequests.Count(r => r.Enrollment!.CourseId == c.Id && r.Status == CertificateStatus.Pending),
                c.Sessions.Where(s => s.StartsAt >= now).Min(s => (DateTime?)s.StartsAt)))
            .ToListAsync(ct);
    }

    public async Task<Result<CourseOverview>> CourseOverviewAsync(int courseId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var now = time.UtcNow;
        var active = await db.Enrollments.CountAsync(e => e.CourseId == courseId && e.Status == EnrollmentStatus.Active, ct);
        var inPerson = await db.Enrollments.CountAsync(e => e.CourseId == courseId && e.Status == EnrollmentStatus.Active && e.Mode == StudyMode.InPerson, ct);
        var lessons = await db.Lessons.CountAsync(l => l.CourseId == courseId, ct);
        var quizzes = await db.Quizzes.CountAsync(q => q.CourseId == courseId, ct);
        var assignments = await db.Assignments.CountAsync(a => a.CourseId == courseId, ct);
        var toGrade = await db.Submissions.CountAsync(s => s.Assignment!.CourseId == courseId && s.Score == null, ct);
        var certificates = await db.CertificateRequests.CountAsync(r => r.Enrollment!.CourseId == courseId && r.Status == CertificateStatus.Pending, ct);
        var upcoming = await db.ClassSessions.CountAsync(s => s.CourseId == courseId && s.EndsAt >= now, ct);
        var next = (await db.ClassSessions.AsNoTracking().Where(s => s.CourseId == courseId && s.EndsAt >= now)
                .OrderBy(s => s.StartsAt).Take(3).Select(s => new { s.StartsAt, s.EndsAt, s.Topic, s.Id }).ToListAsync(ct))
            .Select(s => (s.StartsAt, s.EndsAt, s.Topic, s.Id)).ToList();

        return new CourseOverview(course.Value, active, inPerson, lessons, quizzes, assignments, toGrade, certificates, upcoming, next);
    }
}
