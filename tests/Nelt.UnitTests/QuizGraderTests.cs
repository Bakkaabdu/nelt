using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.UnitTests;

public class QuizGraderTests
{
    [Fact]
    public void Single_choice_awards_full_points_only_for_the_correct_option()
    {
        Assert.Equal(2m, QuizGrader.Score(QuestionType.SingleChoice, 2, [5], [5]));
        Assert.Equal(0m, QuizGrader.Score(QuestionType.SingleChoice, 2, [5], [6]));
        Assert.Equal(0m, QuizGrader.Score(QuestionType.SingleChoice, 2, [5], [5, 6]));
    }

    [Fact]
    public void Multiple_choice_gives_partial_credit_and_penalises_wrong_picks()
    {
        int[] correct = [1, 2, 3, 4];

        Assert.Equal(4m, QuizGrader.Score(QuestionType.MultipleChoice, 4, correct, [1, 2, 3, 4]));
        Assert.Equal(2m, QuizGrader.Score(QuestionType.MultipleChoice, 4, correct, [1, 2]));
        Assert.Equal(1m, QuizGrader.Score(QuestionType.MultipleChoice, 4, correct, [1, 2, 9]));
        Assert.Equal(0m, QuizGrader.Score(QuestionType.MultipleChoice, 4, correct, [1, 8, 9]));
    }

    [Fact]
    public void Empty_answers_score_zero()
    {
        Assert.Equal(0m, QuizGrader.Score(QuestionType.TrueFalse, 1, [1], []));
    }

    [Theory]
    [InlineData(7, 10, 70)]
    [InlineData(1, 3, 33.33)]
    [InlineData(0, 0, 0)]
    public void Percent_is_rounded_to_two_decimals(double earned, double total, double expected)
    {
        Assert.Equal((decimal)expected, QuizGrader.Percent((decimal)earned, (decimal)total));
    }
}
