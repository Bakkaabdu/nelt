using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

public enum QuizKind
{
    [Display(Name = "Quiz")] Quiz = 1,
    [Display(Name = "Final exam")] FinalExam = 2,
}

/// <summary>Which students a quiz is intended for.</summary>
public enum QuizAudience
{
    [Display(Name = "All students")] All = 0,
    [Display(Name = "Online students")] Online = 1,
    [Display(Name = "In-person students")] InPerson = 2,
}

public enum QuestionType
{
    [Display(Name = "Single choice")] SingleChoice = 1,
    [Display(Name = "Multiple choice")] MultipleChoice = 2,
    [Display(Name = "True / false")] TrueFalse = 3,
}

/// <summary>Whether an attempt was taken on the platform or recorded by the instructor (e.g. a paper quiz in class).</summary>
public enum AttemptSource
{
    [Display(Name = "On the platform")] Online = 1,
    [Display(Name = "Recorded by instructor")] Recorded = 2,
}

public static class QuizAudienceExtensions
{
    public static bool Includes(this QuizAudience audience, StudyMode mode) => audience switch
    {
        QuizAudience.Online => mode == StudyMode.Online,
        QuizAudience.InPerson => mode == StudyMode.InPerson,
        _ => true,
    };
}
