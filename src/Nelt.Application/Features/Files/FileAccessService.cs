using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Files;

public enum ProtectedFileKind
{
    LessonVideo = 1,
    Material = 2,
    AssignmentAttachment = 3,
    Submission = 4,
}

public sealed record FileDescriptor(string Key, string FileName, string ContentType);

public interface IFileAccessService
{
    /// <summary>Resolves a protected file if (and only if) the current user may read it.</summary>
    Task<FileDescriptor?> ResolveAsync(ProtectedFileKind kind, int id, CancellationToken ct = default);

    /// <summary>Public course cover images (no authorization needed, only published courses).</summary>
    Task<string?> CoverKeyAsync(int courseId, CancellationToken ct = default);
}

internal sealed class FileAccessService(IAppDbContext db, ICurrentUser user) : IFileAccessService
{
    public Task<FileDescriptor?> ResolveAsync(ProtectedFileKind kind, int id, CancellationToken ct = default) => kind switch
    {
        ProtectedFileKind.LessonVideo => LessonVideoAsync(id, ct),
        ProtectedFileKind.Material => MaterialAsync(id, ct),
        ProtectedFileKind.AssignmentAttachment => AttachmentAsync(id, ct),
        ProtectedFileKind.Submission => SubmissionAsync(id, ct),
        _ => Task.FromResult<FileDescriptor?>(null),
    };

    public Task<string?> CoverKeyAsync(int courseId, CancellationToken ct = default)
        => db.Courses.AsNoTracking().Where(c => c.Id == courseId && c.IsPublished).Select(c => c.CoverImageKey).FirstOrDefaultAsync(ct);

    private async Task<FileDescriptor?> LessonVideoAsync(int lessonId, CancellationToken ct)
    {
        var lesson = await db.Lessons.AsNoTracking().Where(l => l.Id == lessonId && l.VideoKey != null)
            .Select(l => new { l.CourseId, l.VideoKey, l.VideoContentType, l.IsPreview, l.IsPublished, CoursePublished = l.Course!.IsPublished })
            .FirstOrDefaultAsync(ct);

        if (lesson is null)
        {
            return null;
        }

        var allowed = (lesson.IsPreview && lesson.IsPublished && lesson.CoursePublished)
                      || await CanManageAsync(lesson.CourseId, ct)
                      || (lesson.IsPublished && await IsStudentOfAsync(lesson.CourseId, ct));

        return allowed ? new FileDescriptor(lesson.VideoKey!, $"lesson-{lessonId}", lesson.VideoContentType ?? "video/mp4") : null;
    }

    private async Task<FileDescriptor?> MaterialAsync(int materialId, CancellationToken ct)
    {
        var m = await db.Materials.AsNoTracking().Where(x => x.Id == materialId)
            .Select(x => new { x.FileKey, x.FileName, x.ContentType, x.LevelId, x.CourseId, x.IsPublished })
            .FirstOrDefaultAsync(ct);

        if (m is null || user.UserId is not { } me)
        {
            return null;
        }

        bool allowed;
        if (user.IsAdmin)
        {
            allowed = true;
        }
        else if (user.IsInRole(Roles.Instructor))
        {
            // Same rule as editing: the instructor teaches this level and, for a course-specific file, that course.
            allowed = m.CourseId is { } courseId
                ? await db.Courses.AnyAsync(c => c.Id == courseId && c.InstructorId == me, ct)
                : await db.Courses.AnyAsync(c => c.LevelId == m.LevelId && c.InstructorId == me, ct);
        }
        else
        {
            allowed = m.IsPublished && await db.Enrollments.AnyAsync(e => e.StudentId == me
                && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed)
                && e.Course!.LevelId == m.LevelId
                && (m.CourseId == null || e.CourseId == m.CourseId), ct);
        }

        return allowed ? new FileDescriptor(m.FileKey, m.FileName, m.ContentType) : null;
    }

    private async Task<FileDescriptor?> AttachmentAsync(int assignmentId, CancellationToken ct)
    {
        var a = await db.Assignments.AsNoTracking().Where(x => x.Id == assignmentId && x.AttachmentKey != null)
            .Select(x => new { x.CourseId, x.AttachmentKey, x.AttachmentName, x.IsPublished })
            .FirstOrDefaultAsync(ct);

        if (a is null)
        {
            return null;
        }

        var allowed = await CanManageAsync(a.CourseId, ct) || (a.IsPublished && await IsStudentOfAsync(a.CourseId, ct));
        return allowed ? new FileDescriptor(a.AttachmentKey!, a.AttachmentName ?? "attachment", Common.FilePolicy.ContentTypeFor(a.AttachmentName ?? string.Empty)) : null;
    }

    private async Task<FileDescriptor?> SubmissionAsync(int submissionId, CancellationToken ct)
    {
        var s = await db.Submissions.AsNoTracking().Where(x => x.Id == submissionId && x.FileKey != null)
            .Select(x => new { x.FileKey, x.FileName, x.Enrollment!.CourseId, x.Enrollment.StudentId })
            .FirstOrDefaultAsync(ct);

        if (s is null)
        {
            return null;
        }

        var allowed = s.StudentId == user.UserId || await CanManageAsync(s.CourseId, ct);
        return allowed ? new FileDescriptor(s.FileKey!, s.FileName ?? "submission", Common.FilePolicy.ContentTypeFor(s.FileName ?? string.Empty)) : null;
    }

    private Task<bool> CanManageAsync(int courseId, CancellationToken ct)
    {
        if (user.IsAdmin)
        {
            return Task.FromResult(true);
        }

        var me = user.UserId;
        return me is null || !user.IsInRole(Roles.Instructor)
            ? Task.FromResult(false)
            : db.Courses.AnyAsync(c => c.Id == courseId && c.InstructorId == me, ct);
    }

    private Task<bool> IsStudentOfAsync(int courseId, CancellationToken ct)
    {
        var me = user.UserId;
        return me is null
            ? Task.FromResult(false)
            : db.Enrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == me
                && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed), ct);
    }
}
