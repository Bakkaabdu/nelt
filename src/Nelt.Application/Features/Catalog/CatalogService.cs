using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Settings;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Catalog;

public interface ICatalogService
{
    Task<LandingPage> GetLandingAsync(CancellationToken ct = default);
    Task<CourseListPage> SearchAsync(CourseFilter filter, CancellationToken ct = default);
    Task<CourseDetails?> GetCourseAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<EventCard>> PublicEventsAsync(CancellationToken ct = default);
}

internal sealed class CatalogService(
    IAppDbContext db,
    ContentCache cache,
    IPlatformSettingsService settings,
    IPlatformTime time,
    ICurrentUser user) : ICatalogService
{
    private const int FeaturedCount = 6;

    private static readonly EnrollmentStatus[] SeatTaking = [EnrollmentStatus.Pending, EnrollmentStatus.Active];

    private IQueryable<Course> Published => db.Courses.AsNoTracking().Where(c => c.IsPublished);

    public Task<LandingPage> GetLandingAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync("landing", async token =>
        {
            var site = await settings.GetAsync(token);
            var tracks = await TracksAsync(token);

            var featured = await ToCards(Published.Where(c => c.IsFeatured)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.StartDate))
                .Take(FeaturedCount).ToListAsync(token);

            if (featured.Count == 0)
            {
                featured = await ToCards(Published.OrderBy(c => c.SortOrder).ThenByDescending(c => c.CreatedAt))
                    .Take(FeaturedCount).ToListAsync(token);
            }

            var events = await UpcomingPublicEvents().Take(4).ToListAsync(token);
            var stats = new PlatformStats(
                await Published.CountAsync(token),
                await db.Levels.CountAsync(token),
                await db.Enrollments.Where(e => e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed)
                    .Select(e => e.StudentId).Distinct().CountAsync(token),
                await UpcomingPublicEvents().CountAsync(token));

            return new LandingPage(site, stats, tracks, featured, events);
        }, ct);

    public async Task<CourseListPage> SearchAsync(CourseFilter filter, CancellationToken ct = default)
    {
        var tracks = await cache.GetOrCreateAsync("tracks", TracksAsync, ct);

        var query = Published;
        if (filter.Language is { } language)
        {
            query = query.Where(c => c.Level!.Language == language);
        }

        if (filter.LevelId is { } levelId)
        {
            query = query.Where(c => c.LevelId == levelId);
        }

        if (filter.Mode is { } mode)
        {
            query = query.Where(c => c.DeliveryMode == mode || c.DeliveryMode == DeliveryMode.Hybrid);
        }

        var courses = await ToCards(query.OrderBy(c => c.Level!.Language).ThenBy(c => c.Level!.Rank).ThenBy(c => c.SortOrder))
            .Take(200).ToListAsync(ct);

        return new CourseListPage(filter, tracks, courses);
    }

    public async Task<CourseDetails?> GetCourseAsync(string slug, CancellationToken ct = default)
    {
        var card = await ToCards(Published.Where(c => c.Slug == slug)).FirstOrDefaultAsync(ct);
        if (card is null)
        {
            return null;
        }

        var extra = await db.Courses.AsNoTracking().Where(c => c.Id == card.Id)
            .Select(c => new
            {
                c.Description,
                Instructor = c.Instructor != null ? c.Instructor.FullName : null,
                c.Level!.Language,
                c.Level.Rank,
                Quizzes = c.Quizzes.Count(q => q.IsPublished && q.Kind == QuizKind.Quiz),
                HasFinal = c.Quizzes.Any(q => q.IsPublished && q.Kind == QuizKind.FinalExam),
            })
            .FirstAsync(ct);

        var lessons = await db.Lessons.AsNoTracking()
            .Where(l => l.CourseId == card.Id && l.IsPublished)
            .OrderBy(l => l.SortOrder)
            .Select(l => new LessonOutline(l.Id, l.Title, l.DurationMinutes, l.IsPreview))
            .ToListAsync(ct);

        var now = time.UtcNow;
        var events = await db.Events.AsNoTracking()
            .Where(e => e.IsPublished && e.CourseId == card.Id && (e.EndsAt ?? e.StartsAt) >= now)
            .OrderBy(e => e.StartsAt).Take(5)
            .Select(e => new EventCard(e.Id, e.Type, e.Title, e.Description, e.Location, e.StartsAt, e.EndsAt, null))
            .ToListAsync(ct);

        var nextLevel = await ToCards(Published.Where(c => c.Level!.Language == extra.Language && c.Level.Rank > extra.Rank)
                .OrderBy(c => c.Level!.Rank).ThenBy(c => c.StartDate))
            .FirstOrDefaultAsync(ct);

        ViewerEnrollment? viewer = null;
        if (user.UserId is { } userId)
        {
            viewer = await db.Enrollments.AsNoTracking()
                .Where(e => e.CourseId == card.Id && e.StudentId == userId)
                .Select(e => new ViewerEnrollment(e.Id, e.Status, e.Mode))
                .FirstOrDefaultAsync(ct);
        }

        var site = await settings.GetAsync(ct);
        return new CourseDetails(card, extra.Description, extra.Instructor, lessons, extra.Quizzes, extra.HasFinal, events,
            nextLevel, viewer, site.PaymentInstructions, site.Currency);
    }

    public Task<IReadOnlyList<EventCard>> PublicEventsAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync<IReadOnlyList<EventCard>>("events:public",
            async token => await UpcomingPublicEvents().Take(100).ToListAsync(token), ct, TimeSpan.FromMinutes(2));

    private IQueryable<EventCard> UpcomingPublicEvents()
    {
        var now = time.UtcNow;
        return db.Events.AsNoTracking()
            .Where(e => e.IsPublished && e.IsPublic && (e.EndsAt ?? e.StartsAt) >= now)
            .OrderBy(e => e.StartsAt)
            .Select(e => new EventCard(e.Id, e.Type, e.Title, e.Description, e.Location, e.StartsAt, e.EndsAt,
                e.Course != null ? e.Course.Title : null));
    }

    private async Task<IReadOnlyList<LanguageTrack>> TracksAsync(CancellationToken ct)
    {
        var levels = await db.Levels.AsNoTracking()
            .OrderBy(l => l.Language).ThenBy(l => l.Rank)
            .Select(l => new { l.Id, l.Language, l.Code, l.Name, Courses = l.Courses.Count(c => c.IsPublished) })
            .ToListAsync(ct);

        return levels.GroupBy(l => l.Language)
            .Select(g => new LanguageTrack(g.Key, g.Select(l => new LevelTile(l.Id, l.Code, l.Name, l.Courses)).ToList()))
            .ToList();
    }

    private static IQueryable<CourseCard> ToCards(IQueryable<Course> query)
        => query.Select(c => new CourseCard(
            c.Id,
            c.Slug,
            c.Title,
            c.Summary,
            c.ScheduleNote,
            c.Level!.Language,
            c.Level.Code,
            c.Level.Name,
            c.DeliveryMode,
            c.Price,
            c.StartDate,
            c.EndDate,
            c.TotalHours,
            c.CoverImageKey,
            c.Capacity == null ? null : c.Capacity - c.Enrollments.Count(e => SeatTaking.Contains(e.Status))));
}
