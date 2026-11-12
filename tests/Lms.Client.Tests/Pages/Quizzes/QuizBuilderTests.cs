using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages.Quizzes;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Quizzes;

public sealed class QuestionRulesTests
{
    private static QuestionDraft Q(string type, params bool[] correct)
    {
        var q = new QuestionDraft { Type = type, Text = "Q", Points = 1m };
        for (var i = 0; i < correct.Length; i++) q.Options.Add(new OptionDraft { Text = "o" + i, IsCorrect = correct[i] });
        return q;
    }

    [Theory]
    [InlineData(QuestionTypes.SingleChoice, "Quiz_Err_OneCorrect", false, false)]
    [InlineData(QuestionTypes.SingleChoice, "Quiz_Err_OneCorrect", true, true)]
    [InlineData(QuestionTypes.SingleChoice, null, true, false)]
    [InlineData(QuestionTypes.MultipleChoice, "Quiz_Err_AtLeastOneCorrect", false, false)]
    [InlineData(QuestionTypes.MultipleChoice, null, true, true)]
    [InlineData(QuestionTypes.TrueFalse, null, true, false)]
    [InlineData(QuestionTypes.TrueFalse, "Quiz_Err_OneCorrect", false, false)]
    public void Option_rules_per_type(string type, string? expected, params bool[] correct) =>
        Assert.Equal(expected, QuestionRules.Validate(Q(type, correct)));

    [Fact]
    public void Choice_questions_need_two_options_and_essays_none()
    {
        Assert.Equal("Quiz_Err_TwoOptions", QuestionRules.Validate(Q(QuestionTypes.MultipleChoice, true)));
        Assert.Null(QuestionRules.Validate(Q(QuestionTypes.Essay)));
        Assert.Equal("Quiz_Err_Options", QuestionRules.Validate(Q(QuestionTypes.Essay, true)));
        Assert.Equal("Quiz_Err_Text", QuestionRules.Validate(new QuestionDraft { Type = QuestionTypes.Essay, Text = " " }));
        Assert.Equal("Quiz_Err_Points", QuestionRules.Validate(new QuestionDraft { Type = QuestionTypes.Essay, Text = "x", Points = 0m }));
    }
}

public sealed class QuizBuilderTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly FakeQuizzesApi _quizzes = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public QuizBuilderTests() => Services.AddSingleton<IQuizzesApi>(_quizzes);

    private async Task<IRenderedComponent<QuizBuilderPage>> OpenAsync(Guid? id = null, string culture = "en", params string[] permissions)
    {
        var page = await StartAsync(id, culture, permissions);
        page.WaitForElement("[data-testid=quiz-heading]", Wait);
        return page;
    }

    private async Task<IRenderedComponent<QuizBuilderPage>> StartAsync(Guid? id, string culture, string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions.Length > 0 ? permissions : [Permissions.Quizzes.View, Permissions.Quizzes.Manage]);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        return Render<QuizBuilderPage>(p => p.Add(x => x.Id, id ?? FakeQuizzesApi.QuizId));
    }

    private static async Task SelectTypeAsync(IRenderedComponent<QuizBuilderPage> page, int index, string type) =>
        await page.InvokeAsync(() => page.FindComponents<MudSelect<string>>()
            .First(s => s.Markup.Contains($"question-type-{index}")).Instance.ValueChanged.InvokeAsync(type));

    private static async Task SetDateAsync(IRenderedComponent<QuizBuilderPage> page, string label, DateTime date) =>
        await page.InvokeAsync(() => page.FindComponents<MudDatePicker>().First(d => d.Instance.Label == label).Instance.DateChanged.InvokeAsync(date));

    [Fact]
    public async Task Builder_shows_the_questions_with_total_points()
    {
        var page = await OpenAsync();

        Assert.Equal(2, page.FindAll("[data-testid^=question-text-]").Count);
        Assert.Contains("2 questions · 5 points", page.Find("[data-testid=quiz-total]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=quiz-unsaved]"));
    }

    [Fact]
    public async Task Mcq_needs_one_correct_option()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=quiz-add-question]").Click();
        page.Find("[data-testid=question-text-2]").Input("Pick one");
        page.Find("[data-testid=option-text-2-0]").Input("Yes");
        page.Find("[data-testid=option-text-2-1]").Input("No");

        page.Find("[data-testid=quiz-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("Mark exactly one correct answer", page.Find("[data-testid=question-error-2]").TextContent), Wait);
        Assert.DoesNotContain("add-question", _quizzes.Calls);
    }

    [Fact]
    public async Task Multi_requires_two_or_more_options()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=quiz-add-question]").Click();
        await SelectTypeAsync(page, 2, QuestionTypes.MultipleChoice);
        page.Find("[data-testid=question-text-2]").Input("Pick several");
        page.Find("[data-testid=option-text-2-0]").Input("A");
        page.Find("[data-testid=option-remove-2-1]").Click();

        page.Find("[data-testid=quiz-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("Add at least two options", page.Find("[data-testid=question-error-2]").TextContent), Wait);
        Assert.DoesNotContain("add-question", _quizzes.Calls);
    }

    [Fact]
    public async Task Pool_draw_count_cannot_exceed_pool_size()
    {
        var page = await OpenAsync();

        await page.InvokeAsync(() => page.FindComponents<MudNumericField<int?>>().First(n => n.Instance.Label == "Questions drawn per attempt").Instance.ValueChanged.InvokeAsync(5));
        page.Find("[data-testid=quiz-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("cannot exceed the number of questions", page.Markup), Wait);
        Assert.DoesNotContain("update", _quizzes.Calls);
    }

    [Fact]
    public async Task Close_after_open_validated()
    {
        var page = await OpenAsync();
        await SetDateAsync(page, "Opens", new DateTime(2026, 12, 2));
        await SetDateAsync(page, "Closes", new DateTime(2026, 12, 1));

        page.Find("[data-testid=quiz-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("must close after it opens", page.Find("[data-testid=quiz-close-error]").TextContent), Wait);
        Assert.DoesNotContain("update", _quizzes.Calls);
    }

    [Fact]
    public async Task Leaving_with_unsaved_changes_asks()
    {
        var page = await OpenAsync();
        var nav = Services.GetRequiredService<NavigationManager>();
        var start = nav.Uri;
        page.Find("[data-testid=question-text-0]").Input("Changed text");
        Assert.NotEmpty(page.FindAll("[data-testid=quiz-unsaved]"));

        nav.NavigateTo("/somewhere-else");

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=confirm-no]")), Wait);
        _dialogs.Find("[data-testid=confirm-no]").Click();
        Assert.Equal(start, nav.Uri);
    }

    [Fact]
    public async Task Leaving_a_clean_builder_does_not_ask()
    {
        await OpenAsync();
        var nav = Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("/somewhere-else");

        Assert.EndsWith("/somewhere-else", nav.Uri);
        Assert.Empty(_dialogs.FindAll("[data-testid=confirm-no]"));
    }

    [Fact]
    public async Task Save_sends_new_and_edited_questions_then_clears_the_unsaved_state()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=question-text-0]").Input("Which letter is first?");
        page.Find("[data-testid=quiz-add-question]").Click();
        await SelectTypeAsync(page, 2, QuestionTypes.Essay);
        page.Find("[data-testid=question-text-2]").Input("Explain");

        page.Find("[data-testid=quiz-save]").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=quiz-unsaved]")), Wait);
        Assert.Contains("update-question", _quizzes.Calls);
        Assert.Contains("add-question", _quizzes.Calls);
        Assert.Contains("reorder", _quizzes.Calls);
        var saved = _quizzes.Quizzes[FakeQuizzesApi.QuizId];
        Assert.Equal(3, saved.Questions.Count);
        Assert.Equal("Which letter is first?", saved.Questions[0].Text);
    }

    [Fact]
    public async Task Reorder_buttons_move_a_question()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=question-down-0]").Click();
        page.Find("[data-testid=quiz-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("reorder", _quizzes.Calls), Wait);
        Assert.Equal(QuestionTypes.Essay, _quizzes.Quizzes[FakeQuizzesApi.QuizId].Questions[0].Type);
    }

    [Fact]
    public async Task Quiz_with_attempts_locks_the_structure_and_explains_why()
    {
        var page = await OpenAsync(FakeQuizzesApi.LockedQuizId);

        Assert.NotEmpty(page.FindAll("[data-testid=quiz-has-attempts]"));
        Assert.Contains("4 attempts", page.Find("[data-testid=quiz-has-attempts]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=quiz-add-question]"));
        Assert.True(page.Find("[data-testid=question-remove-0]").HasAttribute("disabled"));
        Assert.True(page.Find("[data-testid=option-correct-0-0]").HasAttribute("disabled"));
        // texts stay editable
        page.Find("[data-testid=question-text-0]").Input("Reworded");
        page.Find("[data-testid=quiz-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("update-question", _quizzes.Calls), Wait);
        Assert.DoesNotContain("add-question", _quizzes.Calls);
        Assert.DoesNotContain("delete-question", _quizzes.Calls);
        Assert.DoesNotContain("reorder", _quizzes.Calls);
    }

    [Fact]
    public async Task Server_quiz_has_attempts_error_is_explained()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=question-text-0]").Input("Changed");
        _quizzes.Failure = new ApiProblemException(409, Lms.Shared.ProblemCodes.quiz_has_attempts, null, null);

        page.Find("[data-testid=quiz-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("Students have started", page.Find("[data-testid=quiz-save-error]").TextContent), Wait);
    }

    [Fact]
    public async Task Preview_tab_shows_the_quiz_as_a_student_would_see_it()
    {
        var page = await OpenAsync();

        await page.InvokeAsync(() => page.FindComponent<MudTabs>().Instance.ActivatePanelAsync(2));

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid^=preview-q-]")), Wait);
        Assert.Contains("preview", _quizzes.Calls);
        Assert.Contains("Which letter comes first?", page.Find("[data-testid=quiz-preview]").TextContent);
        Assert.DoesNotContain("correct", page.Find("[data-testid=quiz-preview]").InnerHtml.Replace("Correct answer", ""));
    }

    [Fact]
    public async Task Builder_is_forbidden_without_manage()
    {
        var page = await StartAsync(null, "en", [Permissions.Quizzes.View]);
        page.WaitForElement("[data-testid=quiz-forbidden]", Wait);
        Assert.Empty(_quizzes.Calls);
    }

    [Fact]
    public async Task Class_course_list_shows_quizzes_and_gates_the_new_button()
    {
        await GrantAsync(Permissions.Quizzes.View);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        RenderProviders();

        var page = Render<CourseQuizzesPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));

        page.WaitForElement("[data-testid=quizzes-heading]", Wait);
        page.WaitForAssertion(() => Assert.Contains("اختبار الحروف", page.Markup), Wait);
        Assert.Empty(page.FindAll("[data-testid=quizzes-new]"));
        Assert.Empty(page.FindAll($"[data-testid=quiz-open-{FakeQuizzesApi.QuizId}]"));
    }

    [Fact]
    public async Task Class_course_list_offers_build_and_new_to_managers()
    {
        await GrantAsync(Permissions.Quizzes.View, Permissions.Quizzes.Manage);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        RenderProviders();

        var page = Render<CourseQuizzesPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));

        page.WaitForElement($"[data-testid=quiz-open-{FakeQuizzesApi.QuizId}]", Wait);
        Assert.NotEmpty(page.FindAll("[data-testid=quizzes-new]"));
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Quizzes.View, Permissions.Quizzes.Manage);
        PlaceContextAtUnit();
        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b =>
        {
            b.OpenComponent<QuizBuilderPage>(0);
            b.AddComponentParameter(1, "Id", FakeQuizzesApi.QuizId);
            b.CloseComponent();
        })));
        layout.WaitForElement("[data-testid=quiz-heading]", Wait);

        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("اختبار الحروف", layout.Find("[data-testid=quiz-heading]").TextContent);
        Assert.Contains("الأسئلة", layout.Markup);
        Assert.Contains("إضافة سؤال", layout.Find("[data-testid=quiz-add-question]").TextContent);
    }
}
