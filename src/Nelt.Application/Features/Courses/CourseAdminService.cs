using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Courses;

public interface ICourseAdminService
{
    Task<IReadOnlyList<CourseAdminRow>> ListAsync(CancellationToken ct = default);
    Task<CourseEditModel?> GetForEditAsync(int id, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(CourseInput input, FileUpload? cover, CancellationToken ct = default);
    Task<Result> UpdateAsync(int id, CourseInput input, FileUpload? cover, bool removeCover, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<UserOption>> InstructorsAsync(CancellationToken ct = default);
}

internal sealed class CourseAdminService(
    IAppDbContext db,
    IFileStorage storage,
    ContentCache cache,
    ILogger<CourseAdminService> logger) : ICourseAdminService
{
    public async Task<IReadOnlyList<CourseAdminRow>> ListAsync(CancellationToken ct = default)
        => await db.Courses.AsNoTracking()
            .OrderBy(c => c.Level!.Language).ThenBy(c => c.Level!.Rank).ThenBy(c => c.SortOrder)
            .Select(c => new CourseAdminRow(
                c.Id, c.Title, c.Level!.Language, c.Level.Code,
                c.Instructor != null ? c.Instructor.FullName : null,
                c.DeliveryMode, c.Price, c.StartDate, c.IsPublished, c.IsFeatured,
                c.Enrollments.Count(e => e.Status == EnrollmentStatus.Active),
                c.Enrollments.Count(e => e.Status == EnrollmentStatus.Pending)))
            .ToListAsync(ct);

    public async Task<CourseEditModel?> GetForEditAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null)
        {
            return null;
        }

        var input = new CourseInput
        {
            LevelId = c.LevelId,
            InstructorId = c.InstructorId,
            Title = c.Title,
            Summary = c.Summary,
            Description = c.Description,
            ScheduleNote = c.ScheduleNote,
            Slug = c.Slug,
            Price = c.Price,
            DeliveryMode = c.DeliveryMode,
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            Capacity = c.Capacity,
            TotalHours = c.TotalHours,
            IsPublished = c.IsPublished,
            IsFeatured = c.IsFeatured,
            SortOrder = c.SortOrder,
            Policy = PolicyInput.From(c.Policy),
        };

        var enrollments = await db.Enrollments.CountAsync(e => e.CourseId == id, ct);
        return new CourseEditModel(input, c.CoverImageKey, enrollments);
    }

    public async Task<Result<int>> CreateAsync(CourseInput input, FileUpload? cover, CancellationToken ct = default)
    {
        var course = new Course();
        var applied = await ApplyAsync(course, input, cover, removeCover: false, ct);
        if (applied.Failed)
        {
            return applied.Error!;
        }

        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return course.Id;
    }

    public async Task<Result> UpdateAsync(int id, CourseInput input, FileUpload? cover, bool removeCover, CancellationToken ct = default)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return Error.NotFound();
        }

        var previousCover = course.CoverImageKey;
        var applied = await ApplyAsync(course, input, cover, removeCover, ct);
        if (applied.Failed)
        {
            return applied;
        }

        await db.SaveChangesAsync(ct);
        cache.Invalidate();

        if (previousCover is not null && previousCover != course.CoverImageKey)
        {
            await TryDeleteAsync(previousCover, ct);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course is null)
        {
            return Error.NotFound();
        }

        if (await db.Enrollments.AnyAsync(e => e.CourseId == id, ct))
        {
            return Error.Conflict("Courses with enrollments cannot be deleted. Unpublish the course instead.");
        }

        var files = await db.Lessons.Where(l => l.CourseId == id && l.VideoKey != null).Select(l => l.VideoKey!)
            .Concat(db.Assignments.Where(a => a.CourseId == id && a.AttachmentKey != null).Select(a => a.AttachmentKey!))
            .ToListAsync(ct);

        await db.ExecuteInTransactionAsync(async token =>
        {
            // Children are removed explicitly: foreign keys are RESTRICT by design to prevent accidental cascades.
            await db.Materials.Where(m => m.CourseId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.CourseId, (int?)null), token);
            await db.Events.Where(e => e.CourseId == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.CourseId, (int?)null), token);
            await db.ClassSessions.Where(s => s.CourseId == id).ExecuteDeleteAsync(token);
            await db.Assignments.Where(a => a.CourseId == id).ExecuteDeleteAsync(token);
            await db.Quizzes.Where(q => q.CourseId == id).ExecuteDeleteAsync(token);
            await db.Lessons.Where(l => l.CourseId == id).ExecuteDeleteAsync(token);
            await db.Courses.Where(c => c.Id == id).ExecuteDeleteAsync(token);
        }, ct);

        cache.Invalidate();
        foreach (var key in files.Append(course.CoverImageKey).OfType<string>())
        {
            await TryDeleteAsync(key, ct);
        }

        return Result.Success();
    }

    public async Task<IReadOnlyList<UserOption>> InstructorsAsync(CancellationToken ct = default)
    {
        return await StaffUsers()
            .OrderBy(u => u.FullName)
            .Select(u => new UserOption(u.Id, u.FullName, u.Email ?? string.Empty))
            .ToListAsync(ct);
    }

    private IQueryable<ApplicationUser> StaffUsers()
        => db.Users.AsNoTracking()
            .Where(u => u.IsActive && db.UserIdsInRoles(Roles.Instructor, Roles.Admin).Contains(u.Id));

    private async Task<Result> ApplyAsync(Course course, CourseInput input, FileUpload? cover, bool removeCover, CancellationToken ct)
    {
        if (!await db.Levels.AnyAsync(l => l.Id == input.LevelId, ct))
        {
            return Error.Validation("Please choose a level.", nameof(CourseInput.LevelId));
        }

        if (input.InstructorId is { } instructorId && !await StaffUsers().AnyAsync(u => u.Id == instructorId, ct))
        {
            return Error.Validation("The selected instructor is not available.", nameof(CourseInput.InstructorId));
        }

        var slug = string.IsNullOrWhiteSpace(input.Slug) ? Slug.Create(input.Title.En, "course") : input.Slug.Trim();
        slug = await UniqueSlugAsync(slug, course.Id, ct);

        if (cover is not null)
        {
            if (FilePolicy.Validate(cover, FileCategory.Image) is { } error)
            {
                return Result.Fail(error with { Field = "cover" });
            }

            course.CoverImageKey = await storage.SaveAsync(cover.Content, "covers", cover.Extension, ct);
        }
        else if (removeCover)
        {
            course.CoverImageKey = null;
        }

        course.Slug = slug;
        course.LevelId = input.LevelId;
        course.InstructorId = input.InstructorId;
        course.Title.CopyFrom(input.Title);
        course.Summary.CopyFrom(input.Summary);
        course.Description.CopyFrom(input.Description);
        course.ScheduleNote.CopyFrom(input.ScheduleNote);
        course.Price = input.Price;
        course.DeliveryMode = input.DeliveryMode;
        course.StartDate = input.StartDate;
        course.EndDate = input.EndDate;
        course.Capacity = input.Capacity;
        course.TotalHours = input.TotalHours;
        course.IsPublished = input.IsPublished;
        course.IsFeatured = input.IsFeatured;
        course.SortOrder = input.SortOrder;
        input.Policy.ApplyTo(course.Policy);
        return Result.Success();
    }

    private async Task<string> UniqueSlugAsync(string slug, int courseId, CancellationToken ct)
    {
        var candidate = slug;
        for (var i = 2; await db.Courses.AnyAsync(c => c.Slug == candidate && c.Id != courseId, ct); i++)
        {
            candidate = $"{slug}-{i}";
        }

        return candidate;
    }

    private async Task TryDeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete stored file {Key}", key);
        }
    }
}
