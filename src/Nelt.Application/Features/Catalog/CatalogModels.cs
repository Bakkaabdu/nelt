using Nelt.Application.Features.Settings;
using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Catalog;

public sealed record CourseCard(
    int Id,
    string Slug,
    LocalizedText Title,
    LocalizedText Summary,
    LocalizedText ScheduleNote,
    TargetLanguage Language,
    string LevelCode,
    LocalizedText LevelName,
    DeliveryMode DeliveryMode,
    decimal Price,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? TotalHours,
    string? CoverImageKey,
    int? SeatsLeft);

public sealed record LevelTile(int LevelId, string Code, LocalizedText Name, int CourseCount);

public sealed record LanguageTrack(TargetLanguage Language, IReadOnlyList<LevelTile> Levels)
{
    public int CourseCount => Levels.Sum(l => l.CourseCount);
}

public sealed record EventCard(
    int Id,
    EventType Type,
    LocalizedText Title,
    LocalizedText Description,
    string? Location,
    DateTime StartsAt,
    DateTime? EndsAt,
    LocalizedText? CourseTitle);

public sealed record PlatformStats(int Courses, int Levels, int Students, int UpcomingEvents);

public sealed record LandingPage(
    SiteSettings Settings,
    PlatformStats Stats,
    IReadOnlyList<LanguageTrack> Tracks,
    IReadOnlyList<CourseCard> FeaturedCourses,
    IReadOnlyList<EventCard> UpcomingEvents);

public sealed record CourseFilter(TargetLanguage? Language = null, int? LevelId = null, DeliveryMode? Mode = null);

public sealed record CourseListPage(CourseFilter Filter, IReadOnlyList<LanguageTrack> Tracks, IReadOnlyList<CourseCard> Courses);

public sealed record LessonOutline(int Id, string Title, int? DurationMinutes, bool IsPreview);

public sealed record ViewerEnrollment(int EnrollmentId, EnrollmentStatus Status, StudyMode Mode);

public sealed record CourseDetails(
    CourseCard Card,
    LocalizedText Description,
    string? InstructorName,
    IReadOnlyList<LessonOutline> Lessons,
    int QuizCount,
    bool HasFinalExam,
    IReadOnlyList<EventCard> Events,
    CourseCard? NextLevelCourse,
    ViewerEnrollment? Viewer,
    LocalizedText PaymentInstructions,
    string Currency)
{
    public int TotalMinutes => Lessons.Sum(l => l.DurationMinutes ?? 0);
    public bool IsFull => Card.SeatsLeft is <= 0;
}
