using System.Globalization;
using Bunit;
using Lms.Client.Pages.Quizzes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages.Quizzes;

/// <summary>The student's result follows the review policy; the page never decides what to hide, it shows what the server sent.</summary>
public sealed class QuizResultTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private readonly FakeQuizAttemptsApi _api = new();

    public QuizResultTests()
    {
        Services.AddSingleton<IQuizAttemptsApi>(_api);
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<QuizResultPage>> OpenAsync(string policy)
    {
        _api.ReviewPolicy = policy;
        var attempt = _api.SeedSubmittedAttempt();
        await _api.GradeAsync(attempt, new GradeAttemptRequest([new EssayGradeInput(FakeQuizAttemptsApi.EssayQuestionId, 2m, "Good")]));
        await GrantAsync(Permissions.Quizzes.View);
        RenderProviders();
        var page = Render<QuizResultPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, FakeQuizAttemptsApi.QuizId).Add(x => x.AttemptId, attempt));
        page.WaitForElement("[data-testid=quiz-result], [data-testid=result-error], [data-testid=result-forbidden]", Wait);
        return page;
    }

    [Fact]
    public async Task Review_policy_hides_answers()
    {
        var scoreOnly = await OpenAsync(ReviewPolicies.ScoreOnly);

        Assert.NotNull(scoreOnly.Find("[data-testid=result-score]"));
        Assert.Empty(scoreOnly.FindAll("[data-testid=result-review]"));
        Assert.NotNull(scoreOnly.Find("[data-testid=result-no-review]"));
    }

    [Fact]
    public async Task Review_policy_none_hides_even_the_score()
    {
        var page = await OpenAsync(ReviewPolicies.None);

        Assert.Empty(page.FindAll("[data-testid=result-score]"));
        Assert.NotNull(page.Find("[data-testid=result-hidden]"));
    }

    [Fact]
    public async Task Answers_policy_shows_choices_but_not_the_correct_ones()
    {
        var page = await OpenAsync(ReviewPolicies.Answers);

        Assert.NotNull(page.Find("[data-testid=result-review]"));
        Assert.DoesNotContain("correct", page.Find("[data-testid=result-review]").TextContent.Replace("Your", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Good", page.Find($"[data-testid=review-feedback-{FakeQuizAttemptsApi.EssayQuestionId}]").TextContent);
    }

    [Fact]
    public async Task AnswersAndCorrect_policy_marks_the_correct_option()
    {
        var page = await OpenAsync(ReviewPolicies.AnswersAndCorrect);

        Assert.Contains("correct", page.Find($"[data-testid=review-option-{FakeQuizAttemptsApi.CorrectOptionId}]").TextContent);
        Assert.DoesNotContain("correct", page.Find($"[data-testid=review-option-{FakeQuizAttemptsApi.WrongOptionId}]").TextContent);
    }

    [Fact]
    public async Task Pending_essays_hide_the_score_with_an_explanation()
    {
        _api.ReviewPolicy = ReviewPolicies.Answers;
        var attempt = _api.SeedSubmittedAttempt(); // essay not graded yet
        await GrantAsync(Permissions.Quizzes.View);
        RenderProviders();

        var page = Render<QuizResultPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, FakeQuizAttemptsApi.QuizId).Add(x => x.AttemptId, attempt));

        page.WaitForElement("[data-testid=result-pending]", Wait);
        Assert.Empty(page.FindAll("[data-testid=result-score]"));
    }
}
