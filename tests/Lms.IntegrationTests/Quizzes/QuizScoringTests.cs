using Lms.Api.Features.Quizzes;
using Lms.Domain.Quizzes;
using Lms.Shared.Quizzes;

namespace Lms.IntegrationTests.Quizzes;

public sealed class QuizScoringTests
{
    private static (QuizQuestion Question, List<QuizOption> Options) Choice(string type, decimal points, params bool[] correct)
    {
        var quiz = (Quiz)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Quiz));
        var question = new QuizQuestion(quiz, 1, type, "Q", points);
        return (question, correct.Select((c, i) => new QuizOption(question, i + 1, "o" + i, c)).ToList());
    }

    [Fact]
    public void Single_choice_scores_full_points_only_for_the_one_correct_option()
    {
        var (q, options) = Choice(QuestionTypes.SingleChoice, 2, false, true, false);

        Assert.Equal(2m, QuizScoring.GradeObjective(q, options, [options[1].Id]));
        Assert.Equal(0m, QuizScoring.GradeObjective(q, options, [options[0].Id]));
        Assert.Equal(0m, QuizScoring.GradeObjective(q, options, []));
        Assert.Equal(0m, QuizScoring.GradeObjective(q, options, [options[1].Id, options[0].Id]));
    }

    [Fact]
    public void Multiple_choice_is_all_or_nothing()
    {
        var (q, options) = Choice(QuestionTypes.MultipleChoice, 3, true, false, true);

        Assert.Equal(3m, QuizScoring.GradeObjective(q, options, [options[0].Id, options[2].Id]));
        Assert.Equal(0m, QuizScoring.GradeObjective(q, options, [options[0].Id]));
        Assert.Equal(0m, QuizScoring.GradeObjective(q, options, [options[0].Id, options[1].Id, options[2].Id]));
        Assert.Equal(0m, QuizScoring.GradeObjective(q, options, [options[0].Id, options[0].Id]));
    }

    [Fact]
    public void Essays_are_not_graded_by_the_server()
    {
        var (q, options) = Choice(QuestionTypes.Essay, 5);

        Assert.Null(QuizScoring.GradeObjective(q, options, []));
    }

    [Theory]
    [InlineData(GradingMethods.Highest, 80)]
    [InlineData(GradingMethods.Last, 40)]
    [InlineData(GradingMethods.Average, 60)]
    public void Grading_methods_compare_percentages(string method, decimal expectedPercent)
    {
        // Two attempts with different pools: 8 of 10 (80 %), then 2 of 5 (40 %).
        var final = QuizScoring.Final([new GradedAttempt(1, 8, 10), new GradedAttempt(2, 2, 5)], method, 20);

        Assert.Equal(expectedPercent, final!.Percent);
        Assert.Equal(expectedPercent * 20 / 100, final.Points); // normalised to the quiz total
        Assert.Equal(20m, final.MaxPoints);
    }

    [Fact]
    public void Last_means_the_highest_attempt_number_and_no_attempts_means_no_score()
    {
        Assert.Equal(100m, QuizScoring.Final([new GradedAttempt(3, 5, 5), new GradedAttempt(1, 0, 5)], GradingMethods.Last, 5)!.Percent);
        Assert.Null(QuizScoring.Final([], GradingMethods.Highest, 5));
        Assert.Equal(0m, new GradedAttempt(1, 0, 0).Percent); // no division by zero
    }
}
