using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Services;

/// <summary>Everything the evaluator needs to know about one student's performance in one course.</summary>
public sealed record PerformanceSnapshot
{
    public StudyMode Mode { get; init; }

    public int LessonsTotal { get; init; }
    public int LessonsCompleted { get; init; }

    /// <summary>Applicable, published quizzes (final exam excluded).</summary>
    public int QuizzesTotal { get; init; }
    public int QuizzesTaken { get; init; }

    /// <summary>Average of the best score per quiz; quizzes not taken count as 0. Null when the course has no quizzes.</summary>
    public decimal? QuizAverage { get; init; }

    public int AssignmentsTotal { get; init; }
    public int AssignmentsSubmitted { get; init; }
    public int AssignmentsAwaitingGrade { get; init; }

    /// <summary>Average percentage over graded and missing assignments (missing = 0). Null when there are no assignments.</summary>
    public decimal? AssignmentAverage { get; init; }

    public bool HasFinalExam { get; init; }
    public decimal? FinalExamScore { get; init; }

    public int SessionsCounted { get; init; }
    public int SessionsAttended { get; init; }

    public decimal LessonCompletion => LessonsTotal == 0 ? 0 : Math.Round(LessonsCompleted * 100m / LessonsTotal, 1);
    public decimal? AttendanceRate => AttendanceRules.Rate(SessionsAttended, SessionsCounted);
}

public enum CriterionKind
{
    FinalExam = 1,
    OverallScore = 2,
    Attendance = 3,
    Lessons = 4,
    Grading = 5,
}

public enum CriterionState
{
    Met = 1,
    NotMet = 2,
    Pending = 3,
}

public sealed record CriterionResult(CriterionKind Kind, CriterionState State, decimal? Actual, decimal? Required);

public sealed record CompletionEvaluation(
    decimal OverallScore,
    IReadOnlyList<CriterionResult> Criteria,
    bool IsCertificateEligible,
    bool IsNextLevelEligible)
{
    public bool IsProvisional => Criteria.Any(c => c.State == CriterionState.Pending);
}

/// <summary>
/// Decides whether a student passed a course (certificate) and may progress to the next level, from quiz results,
/// the final exam, assignments, lesson completion (online) and attendance (in person).
/// Components that do not exist in a course (e.g. no assignments) are dropped and their weight redistributed.
/// </summary>
public static class CompletionEvaluator
{
    public static CompletionEvaluation Evaluate(CompletionPolicy policy, PerformanceSnapshot p)
    {
        var overall = OverallScore(policy, p);
        var criteria = new List<CriterionResult>();

        // A course without a final exam is judged on its other components; it must not wait for an exam that does not exist.
        if (p.HasFinalExam)
        {
            criteria.Add(FinalExam(policy, p));
        }

        criteria.Add(new(CriterionKind.OverallScore, overall >= policy.PassingScore ? CriterionState.Met : CriterionState.NotMet, overall, policy.PassingScore));

        if (p.Mode == StudyMode.InPerson)
        {
            var rate = p.AttendanceRate;
            var state = rate is null || rate >= policy.MinAttendanceRate ? CriterionState.Met : CriterionState.NotMet;
            criteria.Add(new(CriterionKind.Attendance, state, rate, policy.MinAttendanceRate));
        }
        else if (policy.RequireAllLessons && p.LessonsTotal > 0)
        {
            var state = p.LessonsCompleted >= p.LessonsTotal ? CriterionState.Met : CriterionState.NotMet;
            criteria.Add(new(CriterionKind.Lessons, state, p.LessonCompletion, 100));
        }

        if (p.AssignmentsAwaitingGrade > 0)
        {
            criteria.Add(new(CriterionKind.Grading, CriterionState.Pending, p.AssignmentsAwaitingGrade, 0));
        }

        var eligible = criteria.All(c => c.State == CriterionState.Met);
        return new CompletionEvaluation(overall, criteria, eligible, eligible && overall >= policy.ProgressionScore);
    }

    public static decimal OverallScore(CompletionPolicy policy, PerformanceSnapshot p)
    {
        decimal weighted = 0, weights = 0;

        void Add(int weight, decimal? value)
        {
            if (weight > 0 && value is not null)
            {
                weighted += weight * value.Value;
                weights += weight;
            }
        }

        Add(policy.QuizWeight, p.QuizAverage);
        Add(policy.AssignmentWeight, p.AssignmentAverage);
        // Until the final exam is taken, the score reflects the work done so far; eligibility stays pending.
        Add(policy.FinalExamWeight, p.HasFinalExam ? p.FinalExamScore : null);

        return weights == 0 ? 0m : Math.Round(weighted / weights, 1, MidpointRounding.AwayFromZero);
    }

    private static CriterionResult FinalExam(CompletionPolicy policy, PerformanceSnapshot p)
    {
        if (p.FinalExamScore is null)
        {
            return new(CriterionKind.FinalExam, CriterionState.Pending, null, policy.MinFinalExamScore);
        }

        var state = p.FinalExamScore >= policy.MinFinalExamScore ? CriterionState.Met : CriterionState.NotMet;
        return new(CriterionKind.FinalExam, state, p.FinalExamScore, policy.MinFinalExamScore);
    }
}
