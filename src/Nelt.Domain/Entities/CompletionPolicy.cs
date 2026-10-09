namespace Nelt.Domain.Entities;

/// <summary>
/// Per-course rules deciding certificate eligibility and progression to the next level.
/// Weights are percentages of the overall score and must add up to 100.
/// </summary>
public sealed class CompletionPolicy
{
    public int QuizWeight { get; set; } = 25;
    public int AssignmentWeight { get; set; } = 25;
    public int FinalExamWeight { get; set; } = 50;

    /// <summary>Minimum overall score (%) to pass the course and request a certificate.</summary>
    public int PassingScore { get; set; } = 60;

    /// <summary>Minimum overall score (%) to be recommended for the next level. Always ≥ <see cref="PassingScore"/>.</summary>
    public int ProgressionScore { get; set; } = 70;

    /// <summary>Minimum final exam score (%).</summary>
    public int MinFinalExamScore { get; set; } = 50;

    /// <summary>Minimum attendance rate (%) required from in-person students.</summary>
    public int MinAttendanceRate { get; set; } = 75;

    /// <summary>Online students must finish every lesson before they are eligible.</summary>
    public bool RequireAllLessons { get; set; } = true;

    public bool HasValidWeights => QuizWeight >= 0 && AssignmentWeight >= 0 && FinalExamWeight >= 0
                                   && QuizWeight + AssignmentWeight + FinalExamWeight == 100;
}
