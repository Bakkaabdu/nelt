using System.ComponentModel.DataAnnotations;
using Nelt.Application.Features.Access;
using Nelt.Application.Features.Quizzes;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Assignments;

public sealed record AssignmentRow(int Id, string Title, DateTime? DueAt, int MaxScore, bool IsPublished, int Submitted, int Graded);

public sealed record AssignmentList(CourseHeader Course, int ActiveStudents, IReadOnlyList<AssignmentRow> Assignments);

public sealed class AssignmentInput
{
    [Required, StringLength(160), Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(8000), Display(Name = "Instructions")]
    public string Instructions { get; set; } = string.Empty;

    [Display(Name = "Lesson")]
    public int? LessonId { get; set; }

    /// <summary>Local (platform time zone) date-time.</summary>
    [Display(Name = "Due")]
    public DateTime? DueAt { get; set; }

    [Range(1, 1000), Display(Name = "Maximum score")]
    public int MaxScore { get; set; } = 100;

    [Display(Name = "Accept late submissions")]
    public bool AllowLateSubmissions { get; set; } = true;

    [Display(Name = "Published")]
    public bool IsPublished { get; set; } = true;
}

public sealed record AssignmentEditModel(CourseHeader Course, int? AssignmentId, AssignmentInput Input, IReadOnlyList<LessonChoice> Lessons, string? AttachmentName);

public sealed record SubmissionRow(
    int EnrollmentId,
    string StudentName,
    StudyMode Mode,
    int? SubmissionId,
    DateTime? SubmittedAt,
    bool IsLate,
    string? Text,
    string? FileName,
    decimal? Score,
    string? Feedback);

public sealed record SubmissionsModel(CourseHeader Course, int AssignmentId, string Title, DateTime? DueAt, int MaxScore, IReadOnlyList<SubmissionRow> Rows);

public sealed class GradeInput
{
    [Range(typeof(decimal), "0", "1000"), Display(Name = "Score")]
    public decimal Score { get; set; }

    [StringLength(4000), Display(Name = "Feedback")]
    public string? Feedback { get; set; }
}

public enum StudentAssignmentState
{
    Open = 1,
    Overdue = 2,
    Submitted = 3,
    Graded = 4,
    Closed = 5,
}

public sealed record StudentAssignmentRow(int Id, string Title, DateTime? DueAt, int MaxScore, StudentAssignmentState State, decimal? Score);

public sealed record StudentAssignmentList(CourseHeader Course, IReadOnlyList<StudentAssignmentRow> Assignments);

public sealed record MySubmission(DateTime SubmittedAt, bool IsLate, string? Text, string? FileName, decimal? Score, string? Feedback, DateTime? GradedAt);

public sealed record StudentAssignmentDetail(
    CourseHeader Course,
    int AssignmentId,
    string Title,
    string Instructions,
    DateTime? DueAt,
    int MaxScore,
    string? AttachmentName,
    StudentAssignmentState State,
    bool CanSubmit,
    MySubmission? Submission);

public sealed class SubmitInput
{
    [StringLength(20000), Display(Name = "Your answer")]
    public string? Text { get; set; }
}
