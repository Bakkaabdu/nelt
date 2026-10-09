using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Access;

/// <summary>Minimal course identity used for page headers and breadcrumbs in the teaching and learning areas.</summary>
public sealed record CourseHeader(int Id, string Slug, LocalizedText Title, string LevelCode, TargetLanguage Language, DeliveryMode DeliveryMode, bool IsPublished);

/// <summary>
/// Central resource-based authorization: admins manage every course, instructors only their own,
/// and students only reach courses they are enrolled in.
/// </summary>
public interface ICourseAccess
{
    IQueryable<Course> ManageableCourses();
    Task<Result<CourseHeader>> ManageAsync(int courseId, CancellationToken ct = default);
    Task<bool> CanManageAsync(int courseId, CancellationToken ct = default);

    /// <summary>Returns the current student's tracked enrollment for a course, requiring active (or completed) access.</summary>
    Task<Result<Enrollment>> StudentEnrollmentAsync(int courseId, CancellationToken ct = default);
}

internal sealed class CourseAccess(IAppDbContext db, ICurrentUser user) : ICourseAccess
{
    public IQueryable<Course> ManageableCourses()
    {
        if (user.IsAdmin)
        {
            return db.Courses;
        }

        if (user.IsInRole(Roles.Instructor) && user.UserId is { } id)
        {
            return db.Courses.Where(c => c.InstructorId == id);
        }

        return db.Courses.Where(_ => false);
    }

    public async Task<Result<CourseHeader>> ManageAsync(int courseId, CancellationToken ct = default)
    {
        var header = await ManageableCourses().AsNoTracking().Where(c => c.Id == courseId)
            .Select(c => new CourseHeader(c.Id, c.Slug, c.Title, c.Level!.Code, c.Level.Language, c.DeliveryMode, c.IsPublished))
            .FirstOrDefaultAsync(ct);

        if (header is not null)
        {
            return header;
        }

        return await db.Courses.AnyAsync(c => c.Id == courseId, ct) ? Error.Forbidden() : Error.NotFound();
    }

    public Task<bool> CanManageAsync(int courseId, CancellationToken ct = default)
        => ManageableCourses().AnyAsync(c => c.Id == courseId, ct);

    public async Task<Result<Enrollment>> StudentEnrollmentAsync(int courseId, CancellationToken ct = default)
    {
        if (user.UserId is not { } studentId)
        {
            return Error.Forbidden();
        }

        var enrollment = await db.Enrollments.Include(e => e.Course).ThenInclude(c => c!.Level)
            .FirstOrDefaultAsync(e => e.CourseId == courseId && e.StudentId == studentId, ct);

        if (enrollment is null)
        {
            return Error.NotFound("You are not enrolled in this course.");
        }

        return enrollment.HasAccess ? enrollment : Error.Forbidden("Your enrollment is not active yet.");
    }
}
