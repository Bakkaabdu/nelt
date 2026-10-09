using Nelt.Domain.Enums;

namespace Nelt.Domain.Services;

/// <summary>Pure grading rules for auto-graded questions.</summary>
public static class QuizGrader
{
    /// <summary>
    /// Single choice / true-false: full points only for exactly the correct option.
    /// Multiple choice: partial credit — each correct pick earns a share, each wrong pick cancels one share (never below zero).
    /// </summary>
    public static decimal Score(QuestionType type, int points, IReadOnlyCollection<int> correctOptionIds, IReadOnlyCollection<int> selectedOptionIds)
    {
        if (points <= 0 || correctOptionIds.Count == 0 || selectedOptionIds.Count == 0)
        {
            return 0m;
        }

        if (type is QuestionType.SingleChoice or QuestionType.TrueFalse)
        {
            return selectedOptionIds.Count == 1 && correctOptionIds.Contains(selectedOptionIds.First()) ? points : 0m;
        }

        var selected = selectedOptionIds.Distinct().ToList();
        var hits = selected.Count(correctOptionIds.Contains);
        var misses = selected.Count - hits;
        var fraction = Math.Max(0m, (decimal)(hits - misses) / correctOptionIds.Count);
        return Math.Round(points * fraction, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal Percent(decimal earned, decimal total)
        => total <= 0 ? 0m : Math.Round(earned / total * 100m, 2, MidpointRounding.AwayFromZero);
}
