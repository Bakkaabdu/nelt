using Nelt.Application.Features.Access;
using Nelt.Application.Features.Catalog;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Learning;

public sealed record StudentCourseCard(
    int CourseId,
    string Slug,
    LocalizedText Title,
    string LevelCode,
    TargetLanguage Language,
    StudyMode Mode,
    EnrollmentStatus Status,
    int LessonsTotal,
    int LessonsCompleted,
    string? CurrentLessonTitle,
    CertificateStatus? Certificate)
{
    public int ProgressPercent => LessonsTotal == 0 ? 0 : (int)Math.Round(LessonsCompleted * 100d / LessonsTotal);
}

public sealed record UpcomingSession(int CourseId, LocalizedText CourseTitle, DateTime StartsAt, DateTime EndsAt, string? Topic, string? Room);

public sealed record DueAssignment(int CourseId, int AssignmentId, string Title, LocalizedText CourseTitle, DateTime? DueAt);

public sealed record StudentDashboard(
    string StudentName,
    IReadOnlyList<StudentCourseCard> Courses,
    IReadOnlyList<UpcomingSession> Sessions,
    IReadOnlyList<DueAssignment> DueAssignments,
    IReadOnlyList<EventCard> Events);

public sealed record LessonItem(int Id, string Title, int? DurationMinutes, bool HasVideo, bool Completed);

public sealed record StudentQuizItem(
    int Id,
    string Title,
    QuizKind Kind,
    int? TimeLimitMinutes,
    int QuestionCount,
    int AttemptsUsed,
    int MaxAttempts,
    decimal? BestScore,
    int PassingScore,
    bool IsOpen,
    DateTime? AvailableUntil);

public sealed record LearnCourseHome(
    CourseHeader Course,
    int EnrollmentId,
    StudyMode Mode,
    EnrollmentStatus Status,
    IReadOnlyList<LessonItem> Lessons,
    int? ContinueLessonId,
    IReadOnlyList<StudentQuizItem> Quizzes,
    int AssignmentsTotal,
    int AssignmentsSubmitted,
    IReadOnlyList<UpcomingSession> Sessions,
    IReadOnlyList<EventCard> Events,
    CompletionEvaluation Evaluation,
    CompletionPolicy Policy)
{
    public int LessonsCompleted => Lessons.Count(l => l.Completed);
    public int ProgressPercent => Lessons.Count == 0 ? 0 : (int)Math.Round(LessonsCompleted * 100d / Lessons.Count);
}

public sealed record LessonPlayer(
    CourseHeader Course,
    int LessonId,
    string Title,
    string? Summary,
    int? DurationMinutes,
    string? EmbedUrl,
    bool HasUploadedVideo,
    string? VideoContentType,
    bool Completed,
    bool IsPreviewMode,
    int? PreviousLessonId,
    int? NextLessonId,
    IReadOnlyList<LessonItem> Outline);

public sealed record MaterialItem(int Id, string Title, string? Description, string FileName, string ContentType, long SizeBytes, bool IsCourseSpecific)
{
    public bool IsAudio => ContentType.StartsWith("audio/", StringComparison.Ordinal);
}

public sealed record MaterialGroup(MaterialType Type, IReadOnlyList<MaterialItem> Items);

public sealed record MaterialShelf(CourseHeader Course, LocalizedText LevelName, IReadOnlyList<MaterialGroup> Groups);

public sealed record AttendanceEntry(DateTime StartsAt, DateTime EndsAt, string? Topic, AttendanceStatus? Status, AttendanceSource? Source, DateTime? CheckInAt);

public sealed record MyAttendance(CourseHeader Course, StudyMode Mode, decimal? Rate, int MinimumRate, IReadOnlyList<AttendanceEntry> Entries);

public sealed record ScheduleItem(DateTime StartsAt, DateTime? EndsAt, LocalizedText Title, string? Location, EventType? EventType, LocalizedText? CourseTitle, bool IsClass);
