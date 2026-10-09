using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.UnitTests;

public class CompletionEvaluatorTests
{
    private static readonly CompletionPolicy Policy = new()
    {
        QuizWeight = 25,
        AssignmentWeight = 25,
        FinalExamWeight = 50,
        PassingScore = 60,
        ProgressionScore = 75,
        MinFinalExamScore = 50,
        MinAttendanceRate = 75,
        RequireAllLessons = true,
    };

    private static PerformanceSnapshot Online(decimal? quiz = 80, decimal? assignments = 80, decimal? final = 80, int lessonsDone = 10) => new()
    {
        Mode = StudyMode.Online,
        LessonsTotal = 10,
        LessonsCompleted = lessonsDone,
        QuizzesTotal = quiz is null ? 0 : 3,
        QuizAverage = quiz,
        AssignmentsTotal = assignments is null ? 0 : 2,
        AssignmentAverage = assignments,
        HasFinalExam = true,
        FinalExamScore = final,
    };

    [Fact]
    public void Strong_online_student_is_eligible_for_certificate_and_next_level()
    {
        var result = CompletionEvaluator.Evaluate(Policy, Online());

        Assert.Equal(80m, result.OverallScore);
        Assert.True(result.IsCertificateEligible);
        Assert.True(result.IsNextLevelEligible);
        Assert.False(result.IsProvisional);
    }

    [Fact]
    public void Passing_but_below_progression_threshold_gets_certificate_only()
    {
        var result = CompletionEvaluator.Evaluate(Policy, Online(quiz: 60, assignments: 60, final: 70));

        Assert.Equal(65m, result.OverallScore);
        Assert.True(result.IsCertificateEligible);
        Assert.False(result.IsNextLevelEligible);
    }

    [Fact]
    public void Missing_final_exam_keeps_evaluation_pending()
    {
        var result = CompletionEvaluator.Evaluate(Policy, Online(final: null));

        Assert.False(result.IsCertificateEligible);
        Assert.True(result.IsProvisional);
        Assert.Equal(CriterionState.Pending, result.Criteria.Single(c => c.Kind == CriterionKind.FinalExam).State);
    }

    [Fact]
    public void Courses_without_a_final_exam_are_judged_on_the_other_components()
    {
        var snapshot = Online(quiz: 80, assignments: 70) with { HasFinalExam = false, FinalExamScore = null };

        var result = CompletionEvaluator.Evaluate(Policy, snapshot);

        Assert.DoesNotContain(result.Criteria, c => c.Kind == CriterionKind.FinalExam);
        Assert.Equal(75m, result.OverallScore);
        Assert.True(result.IsCertificateEligible);
        Assert.True(result.IsNextLevelEligible);
    }

    [Fact]
    public void Final_exam_below_minimum_fails_even_with_high_overall()
    {
        var result = CompletionEvaluator.Evaluate(Policy, Online(quiz: 100, assignments: 100, final: 45));

        Assert.False(result.IsCertificateEligible);
        Assert.Equal(CriterionState.NotMet, result.Criteria.Single(c => c.Kind == CriterionKind.FinalExam).State);
    }

    [Fact]
    public void Online_student_must_finish_all_lessons()
    {
        var result = CompletionEvaluator.Evaluate(Policy, Online(lessonsDone: 9));

        Assert.False(result.IsCertificateEligible);
        Assert.Equal(CriterionState.NotMet, result.Criteria.Single(c => c.Kind == CriterionKind.Lessons).State);
    }

    [Fact]
    public void In_person_student_needs_minimum_attendance_instead_of_lessons()
    {
        var snapshot = Online() with { Mode = StudyMode.InPerson, LessonsCompleted = 0, SessionsCounted = 20, SessionsAttended = 14 };

        var result = CompletionEvaluator.Evaluate(Policy, snapshot);

        Assert.False(result.IsCertificateEligible);
        Assert.Equal(70m, snapshot.AttendanceRate);
        Assert.False(result.Criteria.Any(c => c.Kind == CriterionKind.Lessons));
    }

    [Fact]
    public void Missing_components_are_dropped_and_weights_redistributed()
    {
        // No quizzes and no assignments in the course: the final exam is the whole grade.
        var result = CompletionEvaluator.Evaluate(Policy, Online(quiz: null, assignments: null, final: 72));

        Assert.Equal(72m, result.OverallScore);
    }

    [Fact]
    public void Ungraded_assignments_block_eligibility_until_graded()
    {
        var snapshot = Online() with { AssignmentsAwaitingGrade = 1 };

        var result = CompletionEvaluator.Evaluate(Policy, snapshot);

        Assert.False(result.IsCertificateEligible);
        Assert.True(result.IsProvisional);
    }
}
