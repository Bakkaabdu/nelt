using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Application.Features.Catalog;
using Nelt.Application.Features.Progress;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Learning;

public interface ILearningService
{
    Task<StudentDashboard> DashboardAsync(CancellationToken ct = default);
    Task<Result<LearnCourseHome>> CourseHomeAsync(int courseId, CancellationToken ct = default);
    Task<Result<LessonPlayer>> LessonAsync(int courseId, int lessonId, CancellationToken ct = default);
    Task<Result<LessonPlayer>> PreviewAsync(string slug, int lessonId, CancellationToken ct = default);
    Task<Result> CompleteLessonAsync(int courseId, int lessonId, CancellationToken ct = default);
    Task<Result<MaterialShelf>> MaterialsAsync(int courseId, CancellationToken ct = default);
    Task<Result<MyAttendance>> AttendanceAsync(int courseId, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduleItem>> ScheduleAsync(CancellationToken ct = default);
}

internal sealed class LearningService(
    IAppDbContext db,
    ICurrentUser user,
    ICourseAccess access,
    IPlatformTime time,
    PerformanceCalculator performance) : ILearningService
{
    private static readonly EnrollmentStatus[] Studying = [EnrollmentStatus.Active, EnrollmentStatus.Completed];

    public async Task<StudentDashboard> DashboardAsync(CancellationToken ct = default)
    {
        var studentId = user.RequiredUserId;
        var now = time.UtcNow;

        var name = await db.Users.Where(u => u.Id == studentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? string.Empty;

        var courses = await db.Enrollments.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.Status != EnrollmentStatus.Cancelled)
            .OrderBy(e => e.Status).ThenByDescending(e => e.LastActivityAt ?? e.CreatedAt)
            .Select(e => new StudentCourseCard(
                e.CourseId, e.Course!.Slug, e.Course.Title, e.Course.Level!.Code, e.Course.Level.Language, e.Mode, e.Status,
                e.Course.Lessons.Count(l => l.IsPublished),
                e.LessonProgress.Count(p => p.Lesson!.IsPublished),
                db.Lessons.Where(l => l.Id == e.LastLessonId).Select(l => l.Title).FirstOrDefault(),
                e.Certificate != null ? (CertificateStatus?)e.Certificate.Status : null))
            .ToListAsync(ct);

        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => s.EndsAt >= now && s.Course!.Enrollments.Any(e => e.StudentId == studentId
                && e.Status == EnrollmentStatus.Active && e.Mode == StudyMode.InPerson))
            .OrderBy(s => s.StartsAt).Take(5)
            .Select(s => new UpcomingSession(s.CourseId, s.Course!.Title, s.StartsAt, s.EndsAt, s.Topic, s.Room))
            .ToListAsync(ct);

        var due = await db.Assignments.AsNoTracking()
            .Where(a => a.IsPublished
                && a.Course!.Enrollments.Any(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Active)
                && !a.Submissions.Any(s => s.Enrollment!.StudentId == studentId)
                && (a.DueAt == null || a.DueAt >= now || a.AllowLateSubmissions))
            .OrderBy(a => a.DueAt == null).ThenBy(a => a.DueAt).Take(6)
            .Select(a => new DueAssignment(a.CourseId, a.Id, a.Title, a.Course!.Title, a.DueAt))
            .ToListAsync(ct);

        var events = await StudentEvents(studentId, now).Take(4).ToListAsync(ct);
        return new StudentDashboard(name, courses, sessions, due, events);
    }

    public async Task<Result<LearnCourseHome>> CourseHomeAsync(int courseId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult.Error!;
        }

        var enrollment = enrollmentResult.Value;
        var course = enrollment.Course!;
        var now = time.UtcNow;

        var lessons = await LessonOutlineAsync(courseId, enrollment.Id, ct);
        var continueId = enrollment.LastLessonId is { } last && lessons.Any(l => l.Id == last && !l.Completed)
            ? last
            : lessons.FirstOrDefault(l => !l.Completed)?.Id ?? lessons.FirstOrDefault()?.Id;

        var quizzes = (await db.Quizzes.AsNoTracking()
                .Where(q => q.CourseId == courseId && q.IsPublished)
                .OrderBy(q => q.Kind).ThenBy(q => q.AvailableFrom).ThenBy(q => q.Id)
                .Select(q => new
                {
                    q.Id, q.Title, q.Kind, q.Audience, q.TimeLimitMinutes, q.MaxAttempts, q.PassingScore, q.AvailableFrom, q.AvailableUntil,
                    Questions = q.Questions.Count,
                    Used = q.Attempts.Count(a => a.EnrollmentId == enrollment.Id),
                    Best = q.Attempts.Where(a => a.EnrollmentId == enrollment.Id).Max(a => a.ScorePercent),
                })
                .ToListAsync(ct))
            .Where(q => q.Audience.Includes(enrollment.Mode))
            .Select(q => new StudentQuizItem(q.Id, q.Title, q.Kind, q.TimeLimitMinutes, q.Questions, q.Used, q.MaxAttempts, q.Best, q.PassingScore,
                q.Questions > 0 && (q.AvailableFrom == null || q.AvailableFrom <= now) && (q.AvailableUntil == null || q.AvailableUntil > now),
                q.AvailableUntil))
            .ToList();

        var assignmentsTotal = await db.Assignments.CountAsync(a => a.CourseId == courseId && a.IsPublished, ct);
        var assignmentsSubmitted = await db.Submissions.CountAsync(s => s.EnrollmentId == enrollment.Id && s.Assignment!.IsPublished, ct);

        var sessions = enrollment.Mode == StudyMode.InPerson
            ? await db.ClassSessions.AsNoTracking()
                .Where(s => s.CourseId == courseId && s.EndsAt >= now)
                .OrderBy(s => s.StartsAt).Take(4)
                .Select(s => new UpcomingSession(s.CourseId, course.Title, s.StartsAt, s.EndsAt, s.Topic, s.Room))
                .ToListAsync(ct)
            : [];

        var events = await db.Events.AsNoTracking()
            .Where(e => e.IsPublished && e.CourseId == courseId && (e.EndsAt ?? e.StartsAt) >= now)
            .OrderBy(e => e.StartsAt).Take(4)
            .Select(e => new EventCard(e.Id, e.Type, e.Title, e.Description, e.Location, e.StartsAt, e.EndsAt, null))
            .ToListAsync(ct);

        var snapshot = await performance.ForEnrollmentAsync(courseId, enrollment.Id, ct);
        var evaluation = CompletionEvaluator.Evaluate(course.Policy, snapshot);

        return new LearnCourseHome(Header(course), enrollment.Id, enrollment.Mode, enrollment.Status, lessons, continueId, quizzes,
            assignmentsTotal, assignmentsSubmitted, sessions, events, evaluation, course.Policy);
    }

    public async Task<Result<LessonPlayer>> LessonAsync(int courseId, int lessonId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult.Error!;
        }

        var enrollment = enrollmentResult.Value;
        var lesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lessonId && l.CourseId == courseId && l.IsPublished, ct);
        if (lesson is null)
        {
            return Error.NotFound();
        }

        enrollment.LastLessonId = lesson.Id;
        enrollment.LastActivityAt = time.UtcNow;
        await db.SaveChangesAsync(ct);

        var outline = await LessonOutlineAsync(courseId, enrollment.Id, ct);
        return Player(Header(enrollment.Course!), lesson, outline, isPreview: false);
    }

    public async Task<Result<LessonPlayer>> PreviewAsync(string slug, int lessonId, CancellationToken ct = default)
    {
        var course = await db.Courses.AsNoTracking().Include(c => c.Level)
            .FirstOrDefaultAsync(c => c.Slug == slug && c.IsPublished, ct);
        if (course is null)
        {
            return Error.NotFound();
        }

        var lesson = await db.Lessons.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.CourseId == course.Id && l.IsPublished && l.IsPreview, ct);
        if (lesson is null)
        {
            return Error.NotFound();
        }

        var outline = await db.Lessons.AsNoTracking()
            .Where(l => l.CourseId == course.Id && l.IsPublished && l.IsPreview)
            .OrderBy(l => l.SortOrder)
            .Select(l => new LessonItem(l.Id, l.Title, l.DurationMinutes, l.VideoKey != null || l.VideoUrl != null, false))
            .ToListAsync(ct);

        return Player(Header(course), lesson, outline, isPreview: true);
    }

    public async Task<Result> CompleteLessonAsync(int courseId, int lessonId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult;
        }

        var enrollment = enrollmentResult.Value;
        if (!await db.Lessons.AnyAsync(l => l.Id == lessonId && l.CourseId == courseId && l.IsPublished, ct))
        {
            return Error.NotFound();
        }

        if (await db.LessonProgress.AnyAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lessonId, ct))
        {
            return Result.Success();
        }

        var now = time.UtcNow;
        db.LessonProgress.Add(new LessonProgress { EnrollmentId = enrollment.Id, LessonId = lessonId, CompletedAt = now });
        enrollment.LastActivityAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (db.IsUniqueViolation(ex))
        {
            db.ClearChangeTracker(); // Completed concurrently (e.g. video "ended" event and button click) — already done.
        }

        return Result.Success();
    }

    public async Task<Result<MaterialShelf>> MaterialsAsync(int courseId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult.Error!;
        }

        var course = enrollmentResult.Value.Course!;
        var items = await db.Materials.AsNoTracking()
            .Where(m => m.IsPublished && m.LevelId == course.LevelId && (m.CourseId == null || m.CourseId == courseId))
            .OrderBy(m => m.Type).ThenBy(m => m.SortOrder).ThenBy(m => m.Title)
            .Select(m => new { m.Type, Item = new MaterialItem(m.Id, m.Title, m.Description, m.FileName, m.ContentType, m.SizeBytes, m.CourseId != null) })
            .ToListAsync(ct);

        var groups = items.GroupBy(i => i.Type).Select(g => new MaterialGroup(g.Key, g.Select(x => x.Item).ToList())).ToList();
        return new MaterialShelf(Header(course), course.Level!.Name, groups);
    }

    public async Task<Result<MyAttendance>> AttendanceAsync(int courseId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult.Error!;
        }

        var enrollment = enrollmentResult.Value;
        var entries = await db.ClassSessions.AsNoTracking()
            .Where(s => s.CourseId == courseId)
            .OrderByDescending(s => s.StartsAt)
            .Select(s => new
            {
                s.StartsAt, s.EndsAt, s.Topic,
                Record = s.Records.Where(r => r.EnrollmentId == enrollment.Id).Select(r => new { r.Status, r.Source, r.CheckInAt }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var list = entries.Select(e => new AttendanceEntry(e.StartsAt, e.EndsAt, e.Topic, e.Record?.Status, e.Record?.Source, e.Record?.CheckInAt)).ToList();
        var attended = list.Count(e => e.Status is { } s && s.CountsAsAttended());
        var absent = list.Count(e => e.Status == AttendanceStatus.Absent);
        var rate = AttendanceRules.Rate(attended, attended + absent);

        return new MyAttendance(Header(enrollment.Course!), enrollment.Mode, rate, enrollment.Course!.Policy.MinAttendanceRate, list);
    }

    public async Task<IReadOnlyList<ScheduleItem>> ScheduleAsync(CancellationToken ct = default)
    {
        var studentId = user.RequiredUserId;
        var now = time.UtcNow;
        var horizon = now.AddDays(90);

        var events = await StudentEvents(studentId, now, horizon).Take(100).ToListAsync(ct);

        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => s.EndsAt >= now && s.StartsAt <= horizon
                && s.Course!.Enrollments.Any(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Active && e.Mode == StudyMode.InPerson))
            .OrderBy(s => s.StartsAt).Take(200)
            .Select(s => new { s.StartsAt, s.EndsAt, s.Topic, s.Room, s.Course!.Title })
            .ToListAsync(ct);

        return events
            .Select(e => new ScheduleItem(e.StartsAt, e.EndsAt, e.Title, e.Location, e.Type, e.CourseTitle, IsClass: false))
            .Concat(sessions.Select(s => new ScheduleItem(s.StartsAt, s.EndsAt, s.Topic is null ? s.Title : LocalizedText.Of(s.Topic),
                s.Room, null, s.Title, IsClass: true)))
            .OrderBy(i => i.StartsAt)
            .ToList();
    }

    private IQueryable<EventCard> StudentEvents(Guid studentId, DateTime now, DateTime? until = null)
    {
        var query = db.Events.AsNoTracking()
            .Where(e => e.IsPublished && (e.EndsAt ?? e.StartsAt) >= now
                                      && (e.IsPublic || e.CourseId == null
                                                     || e.Course!.Enrollments.Any(en => en.StudentId == studentId && Studying.Contains(en.Status))));

        if (until is { } horizon)
        {
            query = query.Where(e => e.StartsAt <= horizon);
        }

        return query
            .OrderBy(e => e.StartsAt)
            .Select(e => new EventCard(e.Id, e.Type, e.Title, e.Description, e.Location, e.StartsAt, e.EndsAt,
                e.Course != null ? e.Course.Title : null));
    }

    private async Task<List<LessonItem>> LessonOutlineAsync(int courseId, int enrollmentId, CancellationToken ct)
        => await db.Lessons.AsNoTracking()
            .Where(l => l.CourseId == courseId && l.IsPublished)
            .OrderBy(l => l.SortOrder)
            .Select(l => new LessonItem(l.Id, l.Title, l.DurationMinutes, l.VideoKey != null || l.VideoUrl != null,
                db.LessonProgress.Any(p => p.LessonId == l.Id && p.EnrollmentId == enrollmentId)))
            .ToListAsync(ct);

    private static LessonPlayer Player(CourseHeader course, Lesson lesson, IReadOnlyList<LessonItem> outline, bool isPreview)
    {
        var index = outline.ToList().FindIndex(l => l.Id == lesson.Id);
        return new LessonPlayer(
            course, lesson.Id, lesson.Title, lesson.Summary, lesson.DurationMinutes,
            lesson.VideoKey is null ? VideoEmbed.ToEmbedUrl(lesson.VideoUrl) : null,
            lesson.VideoKey is not null, lesson.VideoContentType,
            index >= 0 && outline[index].Completed,
            isPreview,
            index > 0 ? outline[index - 1].Id : null,
            index >= 0 && index < outline.Count - 1 ? outline[index + 1].Id : null,
            outline);
    }

    internal static CourseHeader Header(Course c)
        => new(c.Id, c.Slug, c.Title, c.Level?.Code ?? string.Empty, c.Level?.Language ?? TargetLanguage.German, c.DeliveryMode, c.IsPublished);
}
