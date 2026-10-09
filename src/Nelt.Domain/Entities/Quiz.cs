using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

public class Quiz : AuditableEntity
{
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public QuizKind Kind { get; set; } = QuizKind.Quiz;
    public QuizAudience Audience { get; set; } = QuizAudience.All;

    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int PassingScore { get; set; } = 60;
    public int MaxAttempts { get; set; } = 1;
    public DateTime? AvailableFrom { get; set; }
    public DateTime? AvailableUntil { get; set; }
    public bool IsPublished { get; set; }

    public ICollection<Question> Questions { get; } = new List<Question>();
    public ICollection<QuizAttempt> Attempts { get; } = new List<QuizAttempt>();

    public bool IsOpenAt(DateTime utcNow)
        => IsPublished
           && (AvailableFrom is null || AvailableFrom <= utcNow)
           && (AvailableUntil is null || AvailableUntil > utcNow);
}

public class Question : Entity
{
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;
    public string Prompt { get; set; } = string.Empty;
    public int Points { get; set; } = 1;
    public int SortOrder { get; set; }
    public ICollection<QuestionOption> Options { get; } = new List<QuestionOption>();
}

public class QuestionOption : Entity
{
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }
}

public class QuizAttempt : Entity
{
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    public AttemptSource Source { get; set; } = AttemptSource.Online;
    public DateTime StartedAt { get; set; }
    public DateTime? DeadlineAt { get; set; }
    public DateTime? SubmittedAt { get; set; }

    public decimal EarnedPoints { get; set; }
    public decimal TotalPoints { get; set; }
    public decimal? ScorePercent { get; set; }

    public Guid? RecordedById { get; set; }
    public string? Note { get; set; }

    public ICollection<AttemptAnswer> Answers { get; } = new List<AttemptAnswer>();

    public bool IsSubmitted => SubmittedAt is not null;
}

public class AttemptAnswer : Entity
{
    public int AttemptId { get; set; }
    public QuizAttempt? Attempt { get; set; }
    public int QuestionId { get; set; }

    /// <summary>Selected option ids, comma separated (small, write-once, never queried relationally).</summary>
    public string SelectedOptionIds { get; set; } = string.Empty;
    public decimal PointsAwarded { get; set; }
}
