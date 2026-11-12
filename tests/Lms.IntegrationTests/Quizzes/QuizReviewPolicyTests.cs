using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Quizzes;
using static Lms.IntegrationTests.Quizzes.AttemptTestKit;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>
/// M4a Task 11 (FR-QZ-02, plan Review Focus 4): a student never receives a correct option unless the review policy allows it AND they have
/// no attempt left or the quiz has closed. These tests read the raw JSON of every student-facing answer.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class QuizReviewPolicyTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<(string Json, AttemptResultDto Result)> ResultAsync(ContentWorld w, Guid attemptId)
    {
        var response = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttemptResult(attemptId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        return (json, System.Text.Json.JsonSerializer.Deserialize<AttemptResultDto>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!);
    }

    [Fact]
    public async Task Review_policy_hides_correct_answers_and_scores_as_it_says()
    {
        var w = await _t.SetUpAsync("RP1");
        var none = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.None));
        var scoreOnly = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.ScoreOnly));
        var answers = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.Answers));

        var rNone = await _t.TakeAsync(w.Student, none);
        var rScore = await _t.TakeAsync(w.Student, scoreOnly);
        var rAnswers = await _t.TakeAsync(w.Student, answers);

        Assert.Null(rNone.Score); // only that it was submitted
        Assert.Null(rNone.Questions);
        Assert.Equal(6m, rScore.Score);
        Assert.Null(rScore.Questions);
        Assert.Equal(6m, rAnswers.Score);
        Assert.NotNull(rAnswers.Questions);
        Assert.All(rAnswers.Questions!.SelectMany(q => q.Options), o => Assert.Null(o.IsCorrect)); // own answers and points, never the key
        Assert.All(rAnswers.Questions!, q => Assert.NotNull(q.AwardedPoints));
        foreach (var attempt in new[] { rNone, rScore, rAnswers })
        {
            var raw = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttemptResult(attempt.AttemptId))).Content.ReadAsStringAsync();
            Assert.DoesNotContain("\"isCorrect\":true", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"isCorrect\":false", raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Correct_options_appear_only_with_the_policy_and_no_attempts_left_or_a_closed_quiz()
    {
        var w = await _t.SetUpAsync("RP2");
        var limited = await _t.BuildAsync(w, QuizSettings(maxAttempts: 2, review: ReviewPolicies.AnswersAndCorrect));
        var unlimited = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.AnswersAndCorrect));

        var first = await _t.TakeAsync(w.Student, limited); // one attempt left
        var (hiddenJson, _) = await ResultAsync(w, first.AttemptId);
        var second = await _t.TakeAsync(w.Student, limited); // none left
        var (shownJson, shown) = await ResultAsync(w, second.AttemptId);
        var (earlierJson, _) = await ResultAsync(w, first.AttemptId); // the earlier attempt is released too: nothing left to pass on

        var open = await _t.TakeAsync(w.Student, unlimited); // unlimited and open: never
        var (openJson, _) = await ResultAsync(w, open.AttemptId);
        await SetQuizWindowAsync(factory, unlimited.Id, null, DateTime.UtcNow.AddMinutes(-1)); // closed
        var (closedJson, closed) = await ResultAsync(w, open.AttemptId);

        Assert.DoesNotContain("isCorrect\":true", hiddenJson);
        Assert.DoesNotContain("isCorrect\":false", hiddenJson);
        Assert.Contains("isCorrect\":true", shownJson);
        Assert.All(shown.Questions!.SelectMany(q => q.Options), o => Assert.NotNull(o.IsCorrect));
        Assert.Contains("isCorrect\":true", earlierJson);
        Assert.DoesNotContain("isCorrect\":true", openJson);
        Assert.Contains("isCorrect\":true", closedJson);
        Assert.All(closed.Questions!.SelectMany(q => q.Options), o => Assert.NotNull(o.IsCorrect));
    }

    [Fact]
    public async Task No_student_facing_answer_carries_the_key_while_an_attempt_is_open()
    {
        var w = await _t.SetUpAsync("RP3");
        var quiz = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.AnswersAndCorrect));

        var start = await _t.StartAsync(w.Student, quiz.Id);
        var startJson = await start.Content.ReadAsStringAsync();
        var attempt = (await start.Content.ReadFromJsonAsync<AttemptDto>())!;
        var resumeJson = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttempt(attempt.Id))).Content.ReadAsStringAsync();
        var summaryJson = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizSummary(quiz.Id))).Content.ReadAsStringAsync();
        var listJson = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseQuizzes(w.ClassCourse.Id))).Content.ReadAsStringAsync();
        var answerJson = await (await _t.AnswerAsync(w.Student, attempt.Id, attempt.Questions[0].QuestionId, [attempt.Questions[0].Options[0].Id], 1)).Content.ReadAsStringAsync();
        var planJson = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseLessonPlan(w.ClassCourse.Id))).Content.ReadAsStringAsync();
        var openResult = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttemptResult(attempt.Id));

        foreach (var json in new[] { startJson, resumeJson, summaryJson, listJson, answerJson, planJson })
        {
            Assert.DoesNotContain("iscorrect", json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(HttpStatusCode.NotFound, openResult.StatusCode); // an attempt in progress has no result yet
    }

    [Fact]
    public async Task Summary_shows_attempts_and_scores_by_policy_and_is_404_for_an_invisible_quiz()
    {
        var w = await _t.SetUpAsync("RP4");
        var shown = await _t.BuildAsync(w, QuizSettings(maxAttempts: 3, review: ReviewPolicies.ScoreOnly));
        var hidden = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.None));
        var draft = await _t.CreateQuizAsync(w.Teacher, w, visibility: Lms.Shared.Content.ModuleVisibilities.Hidden);
        await _t.TakeAsync(w.Student, shown);
        await _t.TakeAsync(w.Student, hidden);

        var summary = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizSummary(shown.Id))).Content.ReadFromJsonAsync<QuizSummaryDto>())!;
        var noScores = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizSummary(hidden.Id))).Content.ReadFromJsonAsync<QuizSummaryDto>())!;

        Assert.Equal((1, 2, true, 6m), (summary.AttemptsUsed, summary.AttemptsLeft, summary.IsOpenNow, summary.FinalScore));
        Assert.Equal(6m, summary.MyAttempts.Single().Score);
        Assert.Null(summary.InProgressAttemptId);
        Assert.Null(noScores.FinalScore); // policy None: nothing to show
        Assert.Null(noScores.MyAttempts.Single().Score);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizSummary(draft.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.QuizSummary(shown.Id))).StatusCode);
    }
}
