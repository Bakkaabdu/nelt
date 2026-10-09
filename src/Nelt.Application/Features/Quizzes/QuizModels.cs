using System.ComponentModel.DataAnnotations;
using Nelt.Application.Features.Access;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Quizzes;

public sealed record QuizRow(
    int Id,
    string Title,
    QuizKind Kind,
    QuizAudience Audience,
    int QuestionCount,
    int TotalPoints,
    int Attempts,
    decimal? AverageScore,
    bool IsPublished,
    DateTime? AvailableFrom,
    DateTime? AvailableUntil);

public sealed record QuizList(CourseHeader Course, IReadOnlyList<QuizRow> Quizzes);

public sealed class QuizInput : IValidatableObject
{
    [Required, StringLength(160), Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000), Display(Name = "Instructions")]
    public string? Instructions { get; set; }

    [Display(Name = "Type")]
    public QuizKind Kind { get; set; } = QuizKind.Quiz;

    [Display(Name = "For")]
    public QuizAudience Audience { get; set; } = QuizAudience.All;

    [Display(Name = "Lesson")]
    public int? LessonId { get; set; }

    [Range(1, 600), Display(Name = "Time limit (minutes)")]
    public int? TimeLimitMinutes { get; set; }

    [Range(0, 100), Display(Name = "Passing score")]
    public int PassingScore { get; set; } = 60;

    [Range(1, 20), Display(Name = "Attempts allowed")]
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Local (platform time zone) date-time from the form.</summary>
    [Display(Name = "Opens")]
    public DateTime? AvailableFrom { get; set; }

    [Display(Name = "Closes")]
    public DateTime? AvailableUntil { get; set; }

    [Display(Name = "Published")]
    public bool IsPublished { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AvailableFrom is not null && AvailableUntil is not null && AvailableUntil <= AvailableFrom)
        {
            yield return new ValidationResult("The closing time must be after the opening time.", [nameof(AvailableUntil)]);
        }
    }
}

public sealed record LessonChoice(int Id, string Title);

public sealed record QuizEditModel(CourseHeader Course, int? QuizId, QuizInput Input, IReadOnlyList<LessonChoice> Lessons, bool HasAttempts);

public sealed class OptionInput
{
    public int? Id { get; set; }

    [StringLength(500)]
    public string? Text { get; set; }

    public bool IsCorrect { get; set; }
}

public sealed class QuestionInput : IValidatableObject
{
    public int? Id { get; set; }

    [Display(Name = "Question type")]
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;

    [Required, StringLength(2000), Display(Name = "Question")]
    public string Prompt { get; set; } = string.Empty;

    [Range(1, 100), Display(Name = "Points")]
    public int Points { get; set; } = 1;

    public List<OptionInput> Options { get; set; } = [];

    /// <summary>For true/false questions: whether "true" is the right answer.</summary>
    public bool TrueIsCorrect { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == QuestionType.TrueFalse)
        {
            yield break;
        }

        var filled = Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).ToList();
        if (filled.Count < 2)
        {
            yield return new ValidationResult("Add at least two answer options.", [nameof(Options)]);
        }

        var correct = filled.Count(o => o.IsCorrect);
        if (Type == QuestionType.SingleChoice && correct != 1)
        {
            yield return new ValidationResult("Mark exactly one correct answer.", [nameof(Options)]);
        }
        else if (Type == QuestionType.MultipleChoice && correct < 1)
        {
            yield return new ValidationResult("Mark at least one correct answer.", [nameof(Options)]);
        }
    }
}

public sealed record OptionView(int Id, string Text, bool IsCorrect);

public sealed record QuestionView(int Id, QuestionType Type, string Prompt, int Points, int SortOrder, IReadOnlyList<OptionView> Options);

public sealed record QuizQuestionsModel(CourseHeader Course, int QuizId, string QuizTitle, QuizKind Kind, bool IsLocked, IReadOnlyList<QuestionView> Questions)
{
    public int TotalPoints => Questions.Sum(q => q.Points);
}

public sealed record QuizResultRow(int EnrollmentId, string StudentName, StudyMode Mode, int Attempts, decimal? BestScore, DateTime? LastSubmittedAt, bool HasRecordedScore);

public sealed record QuizResultsModel(CourseHeader Course, int QuizId, string QuizTitle, QuizKind Kind, QuizAudience Audience, int PassingScore, IReadOnlyList<QuizResultRow> Rows);

public sealed class RecordScoreInput
{
    [Range(1, int.MaxValue)]
    public int EnrollmentId { get; set; }

    [Range(typeof(decimal), "0", "100"), Display(Name = "Score (%)")]
    public decimal Score { get; set; }

    [StringLength(300), Display(Name = "Note")]
    public string? Note { get; set; }
}

// ----- Student side -----

public enum QuizBlockReason
{
    None = 0,
    NotOpen = 1,
    NoAttemptsLeft = 2,
    LessonsIncomplete = 3,
    NotForYourMode = 4,
    NoQuestions = 5,
}

public sealed record QuizIntro(
    CourseHeader Course,
    int QuizId,
    string Title,
    string? Instructions,
    QuizKind Kind,
    int QuestionCount,
    int TotalPoints,
    int? TimeLimitMinutes,
    int PassingScore,
    int AttemptsUsed,
    int MaxAttempts,
    decimal? BestScore,
    int? OpenAttemptId,
    int? LastAttemptId,
    DateTime? AvailableUntil,
    QuizBlockReason BlockReason);

public sealed record TakeOption(int Id, string Text);

public sealed record TakeQuestion(int Id, int Number, QuestionType Type, string Prompt, int Points, IReadOnlyList<TakeOption> Options);

public sealed record AttemptSheet(CourseHeader Course, int AttemptId, int QuizId, string Title, QuizKind Kind, DateTime? DeadlineAt, IReadOnlyList<TakeQuestion> Questions);

public sealed record ReviewOption(int Id, string Text, bool Selected, bool? IsCorrect);

public sealed record ReviewQuestion(int Number, string Prompt, int Points, decimal Awarded, IReadOnlyList<ReviewOption> Options);

public sealed record AttemptReview(
    CourseHeader Course,
    int QuizId,
    string Title,
    QuizKind Kind,
    decimal ScorePercent,
    decimal EarnedPoints,
    decimal TotalPoints,
    int PassingScore,
    DateTime SubmittedAt,
    bool AnswersRevealed,
    bool CanRetry,
    IReadOnlyList<ReviewQuestion> Questions)
{
    public bool Passed => ScorePercent >= PassingScore;
}
