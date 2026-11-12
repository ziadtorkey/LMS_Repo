using System.Globalization;
using Bunit;
using Lms.Client.Pages.Quizzes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Quizzes;

/// <summary>Quiz attempt (M4a T23): start, navigator, server-driven timer, debounced autosave, submit, result and resume.</summary>
public sealed class QuizAttemptTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid Quiz = FakeQuizAttemptsApi.QuizId;

    private readonly FakeQuizAttemptsApi _api = new();
    private readonly ManualTimeProvider _time = new();

    public QuizAttemptTests()
    {
        Services.AddSingleton<IQuizAttemptsApi>(_api);
        Services.AddSingleton<TimeProvider>(_time);
        _api.ServerNow = () => _time.GetUtcNow().UtcDateTime;
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<QuizAttemptPage>> OpenAsync()
    {
        await GrantAsync(Permissions.Quizzes.View, Permissions.Quizzes.Attempt);
        RenderProviders();
        var page = Render<QuizAttemptPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, Quiz));
        page.WaitForElement("[data-testid=quiz-start], [data-testid=quiz-error], [data-testid=quiz-forbidden]", Wait);
        return page;
    }

    private async Task<IRenderedComponent<QuizAttemptPage>> StartAsync()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=quiz-start-button]").Click();
        page.WaitForElement("[data-testid=quiz-attempt]", Wait);
        return page;
    }

    private static Task ChooseAsync(IRenderedComponent<QuizAttemptPage> page, Guid option) =>
        page.InvokeAsync(() => page.FindComponent<MudRadioGroup<Guid?>>().Instance.ValueChanged.InvokeAsync(option));

    [Fact]
    public async Task Start_page_shows_attempts_left_and_time_limit()
    {
        var page = await OpenAsync();

        Assert.Contains("30 minutes", page.Find("[data-testid=quiz-time-limit]").TextContent);
        Assert.Contains("2 attempts left", page.Find("[data-testid=quiz-attempts-left]").TextContent);
    }

    [Fact]
    public async Task Answers_autosave_debounced()
    {
        var page = await StartAsync();

        await ChooseAsync(page, FakeQuizAttemptsApi.WrongOptionId);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId); // changes within the debounce window collapse into one save
        Assert.Empty(_api.Saves);

        _time.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() => Assert.Single(_api.Saves));
        var save = _api.Saves.Single();
        Assert.Equal([FakeQuizAttemptsApi.CorrectOptionId], save.SelectedOptionIds);
        Assert.Equal(2, save.Sequence);
        page.WaitForAssertion(() => Assert.Contains("saved", page.Find("[data-testid=save-state]").TextContent));
    }

    [Fact]
    public async Task Failed_autosave_shows_state_and_retries()
    {
        _api.SaveFailuresLeft = 1;
        var page = await StartAsync();

        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId);
        _time.Advance(TimeSpan.FromSeconds(1));

        page.WaitForElement("[data-testid=save-failed]", Wait);
        page.Find("[data-testid=save-retry]").Click();

        page.WaitForAssertion(() => Assert.Contains("saved", page.Find("[data-testid=save-state]").TextContent));
        Assert.Equal(2, _api.Saves.Count);
        Assert.Equal(_api.Saves[0].Sequence, _api.Saves[1].Sequence); // the same answer, retried
    }

    [Fact]
    public async Task Timer_uses_server_remaining_time()
    {
        _api.TimeLimitMinutes = 2;
        var page = await StartAsync();
        Assert.Equal("02:00", page.Find("[data-testid=attempt-timer-value]").TextContent);

        _time.Advance(TimeSpan.FromSeconds(30));
        page.WaitForAssertion(() => Assert.Equal("01:30", page.Find("[data-testid=attempt-timer-value]").TextContent));

        // The server clock ran 20 s faster than the browser: the next save corrects the display.
        _api.ServerNow = () => _time.GetUtcNow().UtcDateTime.AddSeconds(20);
        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId);
        _time.Advance(TimeSpan.FromSeconds(1));
        page.WaitForAssertion(() => Assert.Equal("01:09", page.Find("[data-testid=attempt-timer-value]").TextContent));
    }

    [Fact]
    public async Task Auto_submits_at_zero()
    {
        _api.TimeLimitMinutes = 1;
        var page = await StartAsync();
        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId);

        _time.Advance(TimeSpan.FromSeconds(61));

        page.WaitForAssertion(() => Assert.Contains("submit", _api.Calls));
        Assert.EndsWith("/result", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Contains(_api.Saves, s => s.SelectedOptionIds.Contains(FakeQuizAttemptsApi.CorrectOptionId)); // the last answer was flushed first
    }

    [Fact]
    public async Task Resume_unfinished_attempt()
    {
        var first = await StartAsync();
        await ChooseAsync(first, FakeQuizAttemptsApi.CorrectOptionId);
        _time.Advance(TimeSpan.FromSeconds(1));
        first.WaitForAssertion(() => Assert.Single(_api.Saves));
        await first.Instance.DisposeAsync();

        var page = Render<QuizAttemptPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, Quiz));
        page.WaitForElement("[data-testid=quiz-resume]", Wait);
        Assert.Empty(page.FindAll("[data-testid=quiz-start-button]"));
        page.Find("[data-testid=quiz-resume]").Click();

        page.WaitForElement("[data-testid=quiz-attempt]", Wait);
        Assert.Equal(1, _api.Calls.Count(c => c == "start")); // no second attempt was created
        Assert.Contains("answered", page.Find("[data-testid=nav-q-1]").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Expired_attempt_on_save_submits_and_leaves()
    {
        _api.TimeLimitMinutes = 1;
        var page = await StartAsync();
        _time.Advance(TimeSpan.FromSeconds(30));
        // The server says the attempt is over (browser clock was wrong): the save is refused with attempt_expired.
        _api.ServerNow = () => _time.GetUtcNow().UtcDateTime.AddMinutes(5);

        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId);
        _time.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() => Assert.Contains("submit", _api.Calls));
    }

    [Fact]
    public async Task Navigator_jumps_between_questions_and_marks_answered()
    {
        var page = await StartAsync();
        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId);

        page.Find("[data-testid=nav-q-3]").Click();

        page.WaitForElement("[data-testid=essay-text]", Wait);
        Assert.Contains("answered", page.Find("[data-testid=nav-q-1]").GetAttribute("aria-label"));
        Assert.DoesNotContain("answered", page.Find("[data-testid=nav-q-2]").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Submit_asks_for_confirmation_with_the_unanswered_count()
    {
        var page = await StartAsync();
        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId);

        page.Find("[data-testid=quiz-submit]").Click();

        Dialogs.WaitForAssertion(() => Assert.Contains("2 questions are unanswered", Dialogs.Markup));
        Assert.DoesNotContain("submit", _api.Calls);
        Dialogs.Find("[data-testid=confirm-yes]").Click();
        page.WaitForAssertion(() => Assert.Contains("submit", _api.Calls));
    }

    [Fact]
    public async Task No_attempts_left_hides_start()
    {
        _api.MaxAttempts = 0;

        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid=quiz-start-button]"));
        Assert.NotNull(page.Find("[data-testid=quiz-unavailable]"));
    }

    [Fact]
    public async Task Submit_is_blocked_while_answers_are_unsaved_and_works_after_a_retry()
    {
        _api.SaveFailuresLeft = 99; // the connection is down
        var page = await StartAsync();
        await ChooseAsync(page, FakeQuizAttemptsApi.CorrectOptionId);
        _time.Advance(TimeSpan.FromSeconds(1));
        page.WaitForElement("[data-testid=save-failed]", Wait);

        page.Find("[data-testid=quiz-submit]").Click();
        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=confirm-yes]")));
        Dialogs.Find("[data-testid=confirm-yes]").Click();

        page.WaitForAssertion(() => Assert.Contains("not saved yet", page.Find("[data-testid=quiz-action-error]").TextContent));
        Assert.DoesNotContain("submit", _api.Calls); // nothing was submitted, no answer was dropped

        _api.SaveFailuresLeft = 0;
        page.Find("[data-testid=save-retry]").Click();
        page.WaitForAssertion(() => Assert.Contains("saved", page.Find("[data-testid=save-state]").TextContent));
        page.Find("[data-testid=quiz-submit]").Click();
        Dialogs.Find("[data-testid=confirm-yes]").Click();
        page.WaitForAssertion(() => Assert.Contains("submit", _api.Calls));
        Assert.Contains(_api.Saves, s => s.SelectedOptionIds.Contains(FakeQuizAttemptsApi.CorrectOptionId));
    }

    [Fact]
    public async Task Timer_announcements_use_localized_units()
    {
        _api.TimeLimitMinutes = 6;
        Culture.Current = new CultureInfo("ar");
        var page = await StartAsync();

        _time.Advance(TimeSpan.FromMinutes(1.5)); // crosses the 5-minute mark

        page.WaitForAssertion(() => Assert.Contains("5 دقيقة", page.Find("[data-testid=attempt-timer-announce]").TextContent));
        Assert.DoesNotContain("min", page.Find("[data-testid=attempt-timer-announce]").TextContent);
    }

    [Fact]
    public async Task Attempt_page_is_arabic_with_left_to_right_numbers()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await StartAsync();

        Assert.Equal("ltr", page.Find("[data-testid=attempt-timer-value]").GetAttribute("dir"));
        Assert.Contains("السؤال 1", page.Find("[data-testid=nav-q-1]").GetAttribute("aria-label"));
    }
}
