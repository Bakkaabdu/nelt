using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Domain.Entities;

namespace Nelt.Application.Features.Lessons;

public sealed record LessonRow(int Id, int SortOrder, string Title, int? DurationMinutes, bool HasUploadedVideo, bool HasExternalVideo, bool IsPreview, bool IsPublished, int CompletedBy);

public sealed record LessonList(CourseHeader Course, IReadOnlyList<LessonRow> Lessons, int ActiveStudents);

public sealed class LessonInput : IValidatableObject
{
    [Required, StringLength(160), Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000), Display(Name = "Summary")]
    public string? Summary { get; set; }

    [StringLength(400), Display(Name = "YouTube or Vimeo link")]
    public string? VideoUrl { get; set; }

    [Range(1, 600), Display(Name = "Duration (minutes)")]
    public int? DurationMinutes { get; set; }

    [Display(Name = "Free preview")]
    public bool IsPreview { get; set; }

    [Display(Name = "Published")]
    public bool IsPublished { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(VideoUrl) && VideoEmbed.ToEmbedUrl(VideoUrl) is null)
        {
            yield return new ValidationResult("Only YouTube and Vimeo links are supported.", [nameof(VideoUrl)]);
        }
    }
}

public sealed record LessonEditModel(CourseHeader Course, int? LessonId, LessonInput Input, bool HasUploadedVideo);

public interface ILessonService
{
    Task<Result<LessonList>> ListAsync(int courseId, CancellationToken ct = default);
    Task<Result<LessonEditModel>> GetForEditAsync(int courseId, int? lessonId, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(int courseId, LessonInput input, FileUpload? video, CancellationToken ct = default);
    Task<Result> UpdateAsync(int courseId, int lessonId, LessonInput input, FileUpload? video, bool removeVideo, CancellationToken ct = default);
    Task<Result> DeleteAsync(int courseId, int lessonId, CancellationToken ct = default);
    Task<Result> MoveAsync(int courseId, int lessonId, int direction, CancellationToken ct = default);
}

internal sealed class LessonService(IAppDbContext db, ICourseAccess access, IFileStorage storage, ContentCache cache) : ILessonService
{
    public async Task<Result<LessonList>> ListAsync(int courseId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == courseId)
            .OrderBy(l => l.SortOrder)
            .Select(l => new LessonRow(l.Id, l.SortOrder, l.Title, l.DurationMinutes, l.VideoKey != null, l.VideoUrl != null,
                l.IsPreview, l.IsPublished, db.LessonProgress.Count(p => p.LessonId == l.Id)))
            .ToListAsync(ct);

        var students = await db.Enrollments.CountAsync(e => e.CourseId == courseId && e.Status == Domain.Enums.EnrollmentStatus.Active, ct);
        return new LessonList(course.Value, lessons, students);
    }

    public async Task<Result<LessonEditModel>> GetForEditAsync(int courseId, int? lessonId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        if (lessonId is null)
        {
            return new LessonEditModel(course.Value, null, new LessonInput(), false);
        }

        var lesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lessonId && l.CourseId == courseId, ct);
        if (lesson is null)
        {
            return Error.NotFound();
        }

        var input = new LessonInput
        {
            Title = lesson.Title,
            Summary = lesson.Summary,
            VideoUrl = lesson.VideoUrl,
            DurationMinutes = lesson.DurationMinutes,
            IsPreview = lesson.IsPreview,
            IsPublished = lesson.IsPublished,
        };
        return new LessonEditModel(course.Value, lesson.Id, input, lesson.VideoKey is not null);
    }

    public async Task<Result<int>> CreateAsync(int courseId, LessonInput input, FileUpload? video, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var lesson = new Lesson
        {
            CourseId = courseId,
            SortOrder = (await db.Lessons.Where(l => l.CourseId == courseId).MaxAsync(l => (int?)l.SortOrder, ct) ?? 0) + 1,
        };

        var result = await ApplyAsync(lesson, input, video, removeVideo: false, ct);
        if (result.Failed)
        {
            return result.Error!;
        }

        db.Lessons.Add(lesson);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteQuietlyAsync(lesson.VideoKey);
            throw;
        }

        cache.Invalidate();
        return lesson.Id;
    }

    public async Task<Result> UpdateAsync(int courseId, int lessonId, LessonInput input, FileUpload? video, bool removeVideo, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId && l.CourseId == courseId, ct);
        if (lesson is null)
        {
            return Error.NotFound();
        }

        var previous = lesson.VideoKey;
        var result = await ApplyAsync(lesson, input, video, removeVideo, ct);
        if (result.Failed)
        {
            return result;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (lesson.VideoKey != previous)
            {
                await storage.DeleteQuietlyAsync(lesson.VideoKey);
            }

            throw;
        }

        cache.Invalidate();
        if (previous is not null && previous != lesson.VideoKey)
        {
            await storage.DeleteQuietlyAsync(previous);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int courseId, int lessonId, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var lesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lessonId && l.CourseId == courseId, ct);
        if (lesson is null)
        {
            return Error.NotFound();
        }

        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LessonProgress.Where(p => p.LessonId == lessonId).ExecuteDeleteAsync(token);
            await db.Enrollments.Where(e => e.LastLessonId == lessonId).ExecuteUpdateAsync(s => s.SetProperty(e => e.LastLessonId, (int?)null), token);
            await db.Quizzes.Where(q => q.LessonId == lessonId).ExecuteUpdateAsync(s => s.SetProperty(q => q.LessonId, (int?)null), token);
            await db.Assignments.Where(a => a.LessonId == lessonId).ExecuteUpdateAsync(s => s.SetProperty(a => a.LessonId, (int?)null), token);
            await db.Lessons.Where(l => l.Id == lessonId).ExecuteDeleteAsync(token);
        }, ct);

        cache.Invalidate();
        if (lesson.VideoKey is not null)
        {
            await storage.DeleteAsync(lesson.VideoKey, ct);
        }

        return Result.Success();
    }

    public async Task<Result> MoveAsync(int courseId, int lessonId, int direction, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var lessons = await db.Lessons.Where(l => l.CourseId == courseId).OrderBy(l => l.SortOrder).ToListAsync(ct);
        var index = lessons.FindIndex(l => l.Id == lessonId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= lessons.Count)
        {
            return Result.Success();
        }

        (lessons[index], lessons[target]) = (lessons[target], lessons[index]);
        for (var i = 0; i < lessons.Count; i++)
        {
            lessons[i].SortOrder = i + 1;
        }

        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    private async Task<Result> ApplyAsync(Lesson lesson, LessonInput input, FileUpload? video, bool removeVideo, CancellationToken ct)
    {
        if (video is not null)
        {
            if (FilePolicy.Validate(video, FileCategory.Video) is { } error)
            {
                return Result.Fail(error with { Field = "video" });
            }

            lesson.VideoKey = await storage.SaveAsync(video.Content, "videos", video.Extension, ct);
            lesson.VideoContentType = FilePolicy.ContentTypeFor(video.Extension);
        }
        else if (removeVideo)
        {
            lesson.VideoKey = null;
            lesson.VideoContentType = null;
        }

        lesson.Title = input.Title.Trim();
        lesson.Summary = string.IsNullOrWhiteSpace(input.Summary) ? null : input.Summary.Trim();
        lesson.VideoUrl = string.IsNullOrWhiteSpace(input.VideoUrl) ? null : input.VideoUrl.Trim();
        lesson.DurationMinutes = input.DurationMinutes;
        lesson.IsPreview = input.IsPreview;
        lesson.IsPublished = input.IsPublished;
        return Result.Success();
    }
}
