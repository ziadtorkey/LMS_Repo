using System.Globalization;
using Bunit;
using Lms.Client.Pages.Quizzes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Quizzes;

/// <summary>Quiz attempts list and essay grading (M4a T24).</summary>
public sealed class QuizGradingTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid Quiz = FakeQuizAttemptsApi.QuizId;
    private static readonly Guid Essay = FakeQuizAttemptsApi.EssayQuestionId;

    private readonly FakeQuizAttemptsApi _api = new() { StaffView = true };

    public QuizGradingTests()
    {
        Services.AddSingleton<IQuizAttemptsApi>(_api);
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<EssayGradingPage>> OpenGradingAsync(Guid attempt, params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<EssayGradingPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, Quiz).Add(x => x.AttemptId, attempt));
        page.WaitForElement("[data-testid=essay-grading], [data-testid=grading-error], [data-testid=grading-forbidden]", Wait);
        return page;
    }

    private static Task SetPointsAsync(IRenderedComponent<EssayGradingPage> page, decimal value) =>
        page.InvokeAsync(() => page.FindComponent<MudNumericField<decimal?>>().Instance.ValueChanged.InvokeAsync(value));

    private static string? PointsText(IRenderedComponent<EssayGradingPage> page) =>
        page.Find($"[data-testid=essay-points-{Essay}]").GetAttribute("value");

    [Fact]
    public async Task Attempts_list_flags_pending_essays_and_links_to_grading()
    {
        var pending = _api.SeedSubmittedAttempt();
        await GrantAsync(Permissions.Quizzes.View, Permissions.Quizzes.Grade);
        RenderProviders();

        var page = Render<QuizAttemptsPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, Quiz));

        page.WaitForElement("[data-testid=attempts-table]", Wait);
        Assert.NotNull(page.Find($"[data-testid=attempt-pending-{pending}]"));
        Assert.EndsWith($"/attempts/{pending}/grade", page.Find($"[data-testid=attempt-grade-{pending}]").GetAttribute("href"));
    }

    [Fact]
    public async Task Viewers_without_Grade_get_no_grade_links()
    {
        var attempt = _api.SeedSubmittedAttempt();
        await GrantAsync(Permissions.Quizzes.View, Permissions.Quizzes.Manage);
        RenderProviders();

        var page = Render<QuizAttemptsPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, Quiz));

        page.WaitForElement("[data-testid=attempts-table]", Wait);
        Assert.Empty(page.FindAll($"[data-testid=attempt-grade-{attempt}]"));
    }

    [Fact]
    public async Task Students_cannot_open_the_attempts_list()
    {
        await GrantAsync(Permissions.Quizzes.View, Permissions.Quizzes.Attempt);
        RenderProviders();

        var page = Render<QuizAttemptsPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, Quiz));

        page.WaitForElement("[data-testid=attempts-forbidden]", Wait);
        Assert.DoesNotContain("list", _api.Calls);
    }

    [Fact]
    public async Task Essay_points_capped_at_question_points()
    {
        var attempt = _api.SeedSubmittedAttempt();
        var page = await OpenGradingAsync(attempt, Permissions.Quizzes.View, Permissions.Quizzes.Grade);

        await SetPointsAsync(page, 99m); // the essay is worth 3

        page.WaitForAssertion(() => Assert.Equal("3", PointsText(page)));
        page.Find("[data-testid=grading-save]").Click();
        page.WaitForAssertion(() => Assert.Single(_api.GradeRequests));
        Assert.Equal(3m, _api.GradeRequests.Single().Grades.Single().Points);
    }

    [Fact]
    public async Task Negative_points_are_floored_at_zero()
    {
        var attempt = _api.SeedSubmittedAttempt();
        var page = await OpenGradingAsync(attempt, Permissions.Quizzes.View, Permissions.Quizzes.Grade);

        await SetPointsAsync(page, -4m);

        page.WaitForAssertion(() => Assert.Equal("0", PointsText(page)));
    }

    [Fact]
    public async Task Finalize_disabled_until_all_essays_graded()
    {
        var attempt = _api.SeedSubmittedAttempt();
        var page = await OpenGradingAsync(attempt, Permissions.Quizzes.View, Permissions.Quizzes.Grade);
        Assert.True(page.Find("[data-testid=grading-finalize]").HasAttribute("disabled"));
        Assert.NotNull(page.Find("[data-testid=grading-finalize-hint]"));

        await SetPointsAsync(page, 2m);

        page.WaitForAssertion(() => Assert.False(page.Find("[data-testid=grading-finalize]").HasAttribute("disabled")));
        page.Find("[data-testid=grading-finalize]").Click();
        page.WaitForAssertion(() => Assert.Single(_api.GradeRequests));
    }

    [Fact]
    public async Task The_comment_is_sent_with_the_points()
    {
        var attempt = _api.SeedSubmittedAttempt();
        var page = await OpenGradingAsync(attempt, Permissions.Quizzes.View, Permissions.Quizzes.Grade);

        await SetPointsAsync(page, 2m);
        page.Find($"[data-testid=essay-comment-{Essay}]").Input("Needs examples");
        page.Find("[data-testid=grading-save]").Click();

        page.WaitForAssertion(() => Assert.Single(_api.GradeRequests));
        Assert.Equal("Needs examples", _api.GradeRequests.Single().Grades.Single().Feedback);
    }

    [Fact]
    public async Task Without_Quizzes_Grade_the_grading_page_is_forbidden()
    {
        var attempt = _api.SeedSubmittedAttempt();
        var page = await OpenGradingAsync(attempt, Permissions.Quizzes.View);

        Assert.NotNull(page.Find("[data-testid=grading-forbidden]"));
    }
}
