using System.Text.Json;
using Lms.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// M4a exit demo 2, quizzes (plan Task 32, #382): a student takes a timed quiz (answers survive a reload), an unfinished attempt
/// is submitted when the quiz closes, and the teacher grades an essay. The quiz is built through the API; the attempt, the
/// grading and the results are driven through the UI, in Arabic (RTL) and English.
/// </summary>
public sealed class QuizTests(E2EFixture fixture) : M4TestBase(fixture)
{
    private const string NeedsM4a = "Needs the M4a assignment, grading and quiz APIs and pages (#391, #410, #421, #399, #415 and the FE pages #400/#403/#405/#406/#408) on main; the rich-text editor assets (#451) are fixed by #456, and the grader still signs in as the Org Admin until #455 seeds a current year";

    private static object QuizSettings(int? timeLimit, DateTime? closesAtUtc, string reviewPolicy) => new
    {
        timeLimitMinutes = timeLimit, maxAttempts = 2, gradingMethod = "Highest", shuffleQuestions = false, shuffleOptions = false,
        opensAtUtc = (DateTime?)null, closesAtUtc, reviewPolicy, drawCount = (int?)null,
    };

    private static async Task<Guid> CreateQuizAsync(E2EApi admin, AssessmentKit.World world, object settings)
    {
        var created = await admin.JsonAsync(await admin.SendAsync("POST", $"/api/class-courses/{world.ClassCourseId}/quizzes", new
        {
            lessonKey = world.LessonKey,
            module = ModuleSettings("اختبار الجغرافيا", "Geography quiz", "Score"),
            descriptionAr = (string?)null,
            descriptionEn = (string?)null,
            settings,
        }));
        return created.GetProperty("id").GetGuid();
    }

    private static async Task AddQuestionAsync(E2EApi admin, Guid quizId, string type, string text, decimal points, params (string Text, bool Correct)[] options)
    {
        var response = await admin.SendAsync("POST", $"/api/quizzes/{quizId}/questions", new
        {
            type, text, points, options = options.Select(o => new { text = o.Text, isCorrect = o.Correct }).ToArray(),
        });
        if (!response.Ok)
        {
            throw new InvalidOperationException($"Adding a question answered {response.Status}: {await response.TextAsync()}");
        }
    }

    /// <summary>Two single-choice questions of 5 points each; "Cairo" and "Nile" are the right answers.</summary>
    private static async Task<Guid> BuildChoiceQuizAsync(E2EApi admin, AssessmentKit.World world, int? timeLimit, DateTime? closesAtUtc, string reviewPolicy)
    {
        var quizId = await CreateQuizAsync(admin, world, QuizSettings(timeLimit, closesAtUtc, reviewPolicy));
        await AddQuestionAsync(admin, quizId, "SingleChoice", "Capital of Egypt?", 5, ("Cairo", true), ("Alexandria", false));
        await AddQuestionAsync(admin, quizId, "SingleChoice", "Longest river?", 5, ("Amazon", false), ("Nile", true));
        return quizId;
    }

    /// <summary>The visible input or textarea of a MudBlazor field, whether the test id sits on the field or on its input.</summary>
    private static ILocator Field(IPage page, string testIdPrefix) => page.Locator($"[data-testid^='{testIdPrefix}']")
        .Locator("xpath=descendant-or-self::*[self::input or self::textarea]").Locator("visible=true").First;

    /// <summary>Waits until the autosave reports everything saved (the answers are sent after a short debounce).</summary>
    private static Task AssertSavedAsync(IPage page, string culture) =>
        Expect(page.GetByTestId("save-state")).ToHaveTextAsync(Resx.In(culture, "Quiz_Saved"));

    /// <summary>Clicks start and returns the id of the attempt the API created (read from its response).</summary>
    private static async Task<Guid> StartAttemptAsync(IPage page)
    {
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByTestId("quiz-start-button").ClickAsync(),
            r => r.Request.Method == "POST" && r.Url.EndsWith("/attempts", StringComparison.Ordinal));
        return (await response.JsonAsync())!.Value.GetProperty("id").GetGuid();
    }

    /// <summary>True when the payload carries a non-null property whose name mentions "correct" anywhere (isCorrect, correctOptionIds...).</summary>
    private static bool LeaksAnswerKey(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().Any(p =>
            (p.Name.Contains("correct", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null) || LeaksAnswerKey(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Any(LeaksAnswerKey),
        _ => false,
    };

    /// <summary>
    /// The student's own API view must hold no answer key: the UI hides it, but a student can call the API. Returns the payload so the
    /// caller can also check the fields the review policy withholds (<c>score</c>, <c>questions</c>).
    /// </summary>
    private static async Task<JsonElement> GetWithoutAnswerKeyAsync(E2EApi api, string path)
    {
        var response = await api.SendAsync("GET", path);
        Assert.True(response.Ok, $"GET {path} answered {response.Status}");
        var json = await api.JsonAsync(response);
        Assert.False(LeaksAnswerKey(json), $"GET {path} leaked correct-answer data: {json}");
        return json;
    }

    private static ILocator Option(IPage page, string text) => page.GetByTestId("quiz-attempt").GetByText(text, new() { Exact = true });

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_takes_timed_quiz_answers_autosave_after_reload_and_scores(string culture)
    {
        var (world, admin, names) = await StartWorldAsync(culture);
        var quizId = await BuildChoiceQuizAsync(admin, world, timeLimit: 30, closesAtUtc: null, reviewPolicy: "Answers");
        await using var student = await OpenStudentAsync(world, names, culture);

        // Start: the time limit is announced and the timer runs.
        await student.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/quizzes/{quizId}");
        await Expect(student.Page.GetByTestId("quiz-time-limit")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var attemptId = await StartAttemptAsync(student.Page);
        await Expect(student.Page.GetByTestId("attempt-timer")).ToBeVisibleAsync();
        await Option(student.Page, "Cairo").ClickAsync();
        await student.Page.GetByTestId("quiz-next").ClickAsync();
        await Option(student.Page, "Amazon").ClickAsync(); // a wrong answer on purpose
        await AssertSavedAsync(student.Page, culture);

        // While the attempt runs the API gives the questions and the saved answers, never which option is right.
        await GetWithoutAnswerKeyAsync(student.Api(Settings.BaseUrl), $"/api/quiz-attempts/{attemptId}");

        // The answers were saved as they were given: after a reload the attempt resumes, and the score below counts what was saved
        // (the student does not answer again). The checked radio is not asserted: MudBlazor's radio state is not exposed reliably.
        await student.Page.ReloadAsync();
        await student.Page.GetByTestId("quiz-resume").ClickAsync();
        await Expect(student.Page.GetByTestId("quiz-attempt")).ToBeVisibleAsync();

        // Submit (confirming the dialog): 5 of 10, with the review the policy allows.
        await student.Page.GetByTestId("quiz-submit").ClickAsync();
        await student.Page.GetByTestId("confirm-yes").ClickAsync();
        await Expect(student.Page.GetByTestId("result-score")).ToContainTextAsync("5");
        await Expect(student.Page.GetByTestId("result-score")).ToContainTextAsync("10");
        await Expect(student.Page.GetByTestId("result-review")).ToBeVisibleAsync();
        await AssertDirectionAsync(student.Page, culture);
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Unfinished_attempt_is_auto_submitted_at_close(string culture)
    {
        var (world, admin, names) = await StartWorldAsync(culture);
        var closes = DateTime.UtcNow.AddSeconds(60);
        var quizId = await BuildChoiceQuizAsync(admin, world, timeLimit: null, closesAtUtc: closes, reviewPolicy: "ScoreOnly");
        await using var student = await OpenStudentAsync(world, names, culture);
        await student.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/quizzes/{quizId}");
        var attemptId = await StartAttemptAsync(student.Page);
        await Option(student.Page, "Cairo").ClickAsync(); // one right answer; the student leaves the attempt unfinished
        await AssertSavedAsync(student.Page, culture);

        // Once the closing time has passed the close job submits what is saved (the demo's dev trigger runs it now).
        await WaitUntilAsync(() => DateTime.UtcNow > closes.AddSeconds(2));
        var run = await admin.SendAsync("POST", $"/api/dev/jobs/quiz-close/{quizId}");
        Assert.True(run.Ok, $"the close job answered {run.Status}");

        // The student sees the quiz closed and the attempt scored on the saved answer: 5 of 10.
        await student.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/quizzes/{quizId}");
        await Expect(student.Page.GetByTestId("quiz-unavailable")).ToBeVisibleAsync();
        await student.Page.Locator("[data-testid^='quiz-attempt-']").First.ClickAsync();
        await Expect(student.Page.GetByTestId("result-score")).ToContainTextAsync("5");
        await Expect(student.Page.GetByTestId("result-score")).ToContainTextAsync("10");

        // ScoreOnly: the score is shown and nothing else, in the UI and in the API payload (no questions, no answer key).
        await Expect(student.Page.GetByTestId("result-review")).ToHaveCountAsync(0);
        var result = await GetWithoutAnswerKeyAsync(student.Api(Settings.BaseUrl), $"/api/quiz-attempts/{attemptId}/result");
        Assert.Equal(JsonValueKind.Null, result.GetProperty("questions").ValueKind);
        await AssertDirectionAsync(student.Page, culture);
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_grades_essay_and_student_sees_result_per_review_policy(string culture)
    {
        var (world, admin, names) = await StartWorldAsync(culture);
        var quizId = await CreateQuizAsync(admin, world, QuizSettings(timeLimit: null, closesAtUtc: null, reviewPolicy: "ScoreOnly"));
        await AddQuestionAsync(admin, quizId, "SingleChoice", "Capital of Egypt?", 4, ("Cairo", true), ("Alexandria", false));
        await AddQuestionAsync(admin, quizId, "Essay", "Describe the Nile.", 6);
        await using var student = await OpenStudentAsync(world, names, culture);
        await using var teacher = await OpenTeacherAsync(culture);

        // The student answers both questions and submits; the result waits for the essay.
        await student.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/quizzes/{quizId}");
        var attemptId = await StartAttemptAsync(student.Page);
        await Option(student.Page, "Cairo").ClickAsync();
        await student.Page.GetByTestId("quiz-next").ClickAsync();
        await Field(student.Page, "essay-text").FillAsync("The Nile flows north through Egypt.");
        await AssertSavedAsync(student.Page, culture);
        await student.Page.GetByTestId("quiz-submit").ClickAsync();
        await student.Page.GetByTestId("confirm-yes").ClickAsync();
        await Expect(student.Page.GetByTestId("result-pending")).ToBeVisibleAsync();

        // While the essay waits, nothing is released: no score and no review in the UI, none in the API payload either.
        await Expect(student.Page.GetByTestId("result-score")).ToHaveCountAsync(0);
        await Expect(student.Page.GetByTestId("result-review")).ToHaveCountAsync(0);
        var pending = await GetWithoutAnswerKeyAsync(student.Api(Settings.BaseUrl), $"/api/quiz-attempts/{attemptId}/result");
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("questions").ValueKind);

        // The teacher opens the attempt in the list, scores the essay 5 of 6 and finalises.
        await teacher.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/quizzes/{quizId}/attempts");
        await Expect(teacher.Page.Locator("[data-testid^='attempt-pending-']")).ToHaveCountAsync(1);
        await teacher.Page.Locator("[data-testid^='attempt-grade-']").First.ClickAsync();
        await Expect(teacher.Page.GetByTestId("essay-grading")).ToBeVisibleAsync();
        await Field(teacher.Page, "essay-points-").FillAsync("5");
        await Field(teacher.Page, "essay-comment-").FillAsync("Good, mention the delta.");
        await teacher.Page.GetByTestId("grading-finalize").ClickAsync();

        // The student now sees 9 of 10 (4 + 5), as the ScoreOnly policy allows: the score and no per-question review.
        await student.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/quizzes/{quizId}");
        await student.Page.Locator("[data-testid^='quiz-attempt-']").First.ClickAsync();
        await Expect(student.Page.GetByTestId("result-score")).ToContainTextAsync("9");
        await Expect(student.Page.GetByTestId("result-score")).ToContainTextAsync("10");
        await Expect(student.Page.GetByTestId("result-review")).ToHaveCountAsync(0);
        var released = await GetWithoutAnswerKeyAsync(student.Api(Settings.BaseUrl), $"/api/quiz-attempts/{attemptId}/result");
        Assert.Equal(9m, released.GetProperty("score").GetDecimal());
        Assert.Equal(JsonValueKind.Null, released.GetProperty("questions").ValueKind);
        await AssertDirectionAsync(student.Page, culture);
    }

    /// <summary>Waits (up to 120 s, polling) until the condition holds; used to let the wall clock pass a closing time.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met within 120 s.");
            }

            await Task.Delay(500);
        }
    }
}
