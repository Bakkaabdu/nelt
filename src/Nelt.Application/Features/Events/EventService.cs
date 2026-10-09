using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Materials;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Events;

public sealed record EventRow(
    int Id,
    EventType Type,
    LocalizedText Title,
    string? Location,
    DateTime StartsAt,
    DateTime? EndsAt,
    LocalizedText? CourseTitle,
    bool IsPublic,
    bool IsPublished);

public sealed class EventInput : IValidatableObject
{
    [Display(Name = "Course")]
    public int? CourseId { get; set; }

    [Display(Name = "Type")]
    public EventType Type { get; set; } = EventType.ConversationSession;

    [LocalizedText(160, RequireEnglish = true), Display(Name = "Title")]
    public LocalizedText Title { get; set; } = new();

    [LocalizedText(4000), Display(Name = "Description")]
    public LocalizedText Description { get; set; } = new();

    [StringLength(200), Display(Name = "Location")]
    public string? Location { get; set; }

    /// <summary>Local (platform time zone) date-time.</summary>
    [Required, Display(Name = "Starts")]
    public DateTime? StartsAt { get; set; }

    [Display(Name = "Ends")]
    public DateTime? EndsAt { get; set; }

    [Display(Name = "Show on the public website")]
    public bool IsPublic { get; set; }

    [Display(Name = "Published")]
    public bool IsPublished { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAt is not null && EndsAt is not null && EndsAt <= StartsAt)
        {
            yield return new ValidationResult("The end must be after the start.", [nameof(EndsAt)]);
        }
    }
}

public sealed record EventEditModel(int? EventId, EventInput Input, IReadOnlyList<LibraryCourse> Courses, bool CourseRequired);

public interface IEventService
{
    Task<IReadOnlyList<EventRow>> ListAsync(bool includePast, CancellationToken ct = default);
    Task<Result<EventEditModel>> GetForEditAsync(int? id, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(EventInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(int id, EventInput input, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>Admins manage every event (including platform-wide ones); instructors manage events of their own courses.</summary>
internal sealed class EventService(IAppDbContext db, ICurrentUser user, IPlatformTime time, ContentCache cache) : IEventService
{
    public async Task<IReadOnlyList<EventRow>> ListAsync(bool includePast, CancellationToken ct = default)
    {
        var now = time.UtcNow;
        var query = Manageable().AsNoTracking();
        query = includePast
            ? query.Where(e => (e.EndsAt ?? e.StartsAt) < now).OrderByDescending(e => e.StartsAt)
            : query.Where(e => (e.EndsAt ?? e.StartsAt) >= now).OrderBy(e => e.StartsAt);

        return await query.Take(200)
            .Select(e => new EventRow(e.Id, e.Type, e.Title, e.Location, e.StartsAt, e.EndsAt, e.Course != null ? e.Course.Title : null, e.IsPublic, e.IsPublished))
            .ToListAsync(ct);
    }

    public async Task<Result<EventEditModel>> GetForEditAsync(int? id, CancellationToken ct = default)
    {
        var courses = await CourseChoicesAsync(ct);
        var courseRequired = !user.IsAdmin;
        if (id is null)
        {
            var start = time.LocalNow.Date.AddDays(7).AddHours(18);
            return new EventEditModel(null, new EventInput { StartsAt = start, EndsAt = start.AddHours(2) }, courses, courseRequired);
        }

        var e = await Manageable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
        {
            return Error.NotFound();
        }

        var input = new EventInput
        {
            CourseId = e.CourseId,
            Type = e.Type,
            Title = e.Title,
            Description = e.Description,
            Location = e.Location,
            StartsAt = time.ToLocal(e.StartsAt),
            EndsAt = e.EndsAt is { } end ? time.ToLocal(end) : null,
            IsPublic = e.IsPublic,
            IsPublished = e.IsPublished,
        };
        return new EventEditModel(e.Id, input, courses, courseRequired);
    }

    public async Task<Result<int>> CreateAsync(EventInput input, CancellationToken ct = default)
    {
        if (await ValidateCourseAsync(input, ct) is { } error)
        {
            return error;
        }

        var entity = new CourseEvent { CreatedById = user.UserId };
        Apply(entity, input);
        db.Events.Add(entity);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return entity.Id;
    }

    public async Task<Result> UpdateAsync(int id, EventInput input, CancellationToken ct = default)
    {
        var entity = await Manageable().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            return Error.NotFound();
        }

        if (await ValidateCourseAsync(input, ct) is { } error)
        {
            return error;
        }

        Apply(entity, input);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await Manageable().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            return Error.NotFound();
        }

        db.Events.Remove(entity);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    private IQueryable<CourseEvent> Manageable()
    {
        if (user.IsAdmin)
        {
            return db.Events;
        }

        var me = user.UserId;
        return db.Events.Where(e => e.Course != null && e.Course.InstructorId == me);
    }

    private async Task<IReadOnlyList<LibraryCourse>> CourseChoicesAsync(CancellationToken ct)
    {
        var query = db.Courses.AsNoTracking();
        if (!user.IsAdmin)
        {
            var me = user.UserId;
            query = query.Where(c => c.InstructorId == me);
        }

        return await query.OrderBy(c => c.Level!.Language).ThenBy(c => c.Level!.Rank)
            .Select(c => new LibraryCourse(c.Id, c.Title)).ToListAsync(ct);
    }

    private async Task<Error?> ValidateCourseAsync(EventInput input, CancellationToken ct)
    {
        if (input.CourseId is null)
        {
            return user.IsAdmin ? null : Error.Validation("Please choose one of your courses.", nameof(EventInput.CourseId));
        }

        var choices = await CourseChoicesAsync(ct);
        return choices.Any(c => c.Id == input.CourseId) ? null : Error.Validation("Please choose one of your courses.", nameof(EventInput.CourseId));
    }

    private void Apply(CourseEvent entity, EventInput input)
    {
        entity.CourseId = input.CourseId;
        entity.Type = input.Type;
        entity.Title.CopyFrom(input.Title);
        entity.Description.CopyFrom(input.Description);
        entity.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        entity.StartsAt = time.ToUtc(input.StartsAt!.Value);
        entity.EndsAt = input.EndsAt is { } end ? time.ToUtc(end) : null;
        entity.IsPublic = input.IsPublic;
        entity.IsPublished = input.IsPublished;
    }
}
