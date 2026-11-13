using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Quizzes;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;
using static Lms.IntegrationTests.Quizzes.AttemptTestKit;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>M4a Task 11 (FR-QZ-02, FR-QZ-03): starting, the timer, autosave and objective grading.</summary>
[Collection(ApiCollection.Name)]
public sealed class AttemptTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<int> AttemptRowsAsync(Guid quizId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<QuizAttempt>().CountAsync(a => a.QuizId == quizId);
    }

    [Fact]
    public async Task Student_starts_an_attempt_and_a_second_start_resumes_it_without_ever_showing_correct_options()
    {
        var w = await _t.SetUpAsync("AT1");
        var quiz = await _t.BuildAsync(w);

        var first = await _t.StartAsync(w.Student, quiz.Id);
        var json = await first.Content.ReadAsStringAsync();
        var second = await _t.StartAsync(w.Student, quiz.Id);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // the attempt in progress is returned
        var a = System.Text.Json.JsonSerializer.Deserialize<AttemptDto>(json, M1CallersJson)!;
        var b = (await second.Content.ReadFromJsonAsync<AttemptDto>())!;
        Assert.Equal(a.Id, b.Id);
        Assert.Equal((1, 3, AttemptStatuses.InProgress), (a.Number, a.Questions.Count, a.Status));
        Assert.DoesNotContain("iscorrect", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await AttemptRowsAsync(quiz.Id));
        var resume = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttempt(a.Id));
        Assert.DoesNotContain("iscorrect", await resume.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static readonly System.Text.Json.JsonSerializerOptions M1CallersJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Quiz_must_be_open_visible_and_started_by_an_enrolled_student()
    {
        var w = await _t.SetUpAsync("AT2");
        var now = DateTime.UtcNow;
        var notYet = await _t.BuildAsync(w, QuizSettings(opens: now.AddHours(1)));
        var closed = await _t.BuildAsync(w, QuizSettings(opens: now.AddDays(-2), closes: now.AddDays(-1)));
        var empty = await _t.CreateQuizAsync(w.Teacher, w); // no questions
        var hidden = await _t.CreateQuizAsync(w.Teacher, w, visibility: Lms.Shared.Content.ModuleVisibilities.Hidden);
        await _t.AddQuestionAsync(w.Teacher, hidden.Id, Single());

        var notYetResponse = await _t.StartAsync(w.Student, notYet.Id);
        var closedResponse = await _t.StartAsync(w.Student, closed.Id);
        var emptyResponse = await _t.StartAsync(w.Student, empty.Id);

        Assert.Equal(ProblemCodes.quiz_not_open, await CodeAsync(notYetResponse));
        Assert.Equal(ProblemCodes.quiz_closed, await CodeAsync(closedResponse));
        Assert.Equal(ProblemCodes.quiz_not_open, await CodeAsync(emptyResponse));
        Assert.Equal(HttpStatusCode.NotFound, (await _t.StartAsync(w.Student, hidden.Id)).StatusCode); // D51: a hidden module is 404
        Assert.Equal(HttpStatusCode.NotFound, (await _t.StartAsync(w.Stranger, notYet.Id)).StatusCode); // not enrolled
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.StartAsync(w.Teacher, notYet.Id)).StatusCode); // staff hold no Quizzes.Attempt
    }

    [Fact]
    public async Task Attempt_limit_enforced_under_concurrency()
    {
        var w = await _t.SetUpAsync("AT3");
        var one = await _t.BuildAsync(w, QuizSettings(maxAttempts: 1));
        var two = await _t.BuildAsync(w, QuizSettings(maxAttempts: 2));

        var parallel = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _t.StartAsync(w.Student, one.Id)));
        var firstId = (await parallel.First(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<AttemptDto>())!.Id;
        await _t.SubmitOkAsync(w.Student, firstId);
        var afterLimit = await _t.StartAsync(w.Student, one.Id);

        await _t.TakeAsync(w.Student, two); // attempt 1 of 2, submitted
        var racing = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _t.StartAsync(w.Student, two.Id)));

        Assert.All(parallel, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await AttemptRowsAsync(one.Id));
        Assert.Equal(HttpStatusCode.Conflict, afterLimit.StatusCode);
        Assert.Equal(ProblemCodes.attempt_limit_reached, await CodeAsync(afterLimit));
        Assert.Equal(2, await AttemptRowsAsync(two.Id)); // the six racing starts made exactly one new attempt
        Assert.DoesNotContain(racing, r => (int)r.StatusCode >= 500);
        var summary = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizSummary(two.Id))).Content.ReadFromJsonAsync<QuizSummaryDto>())!;
        Assert.Equal((2, 0), (summary.AttemptsUsed, summary.AttemptsLeft));
    }

    [Fact]
    public async Task Timer_is_server_side_and_a_late_save_is_409_after_the_grace()
    {
        var w = await _t.SetUpAsync("AT4");
        var quiz = await _t.BuildAsync(w, QuizSettings(timeLimit: 10));
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        var question = attempt.Questions[0];

        Assert.InRange(attempt.RemainingSeconds!.Value, 590, 600);
        Assert.Equal(attempt.StartedAtUtc.AddMinutes(10), attempt.EndsAtUtc);
        await SetEndAsync(factory, attempt.Id, DateTime.UtcNow.AddSeconds(-5)); // inside the 30 s grace
        var inGrace = await _t.AnswerAsync(w.Student, attempt.Id, question.QuestionId, [question.Options[0].Id], 1);
        await SetEndAsync(factory, attempt.Id, DateTime.UtcNow.AddMinutes(-2)); // past it
        var late = await _t.AnswerAsync(w.Student, attempt.Id, question.QuestionId, [question.Options[1].Id], 2);

        Assert.Equal(HttpStatusCode.OK, inGrace.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(ProblemCodes.attempt_expired, await CodeAsync(late));
        var after = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttempt(attempt.Id))).Content.ReadFromJsonAsync<AttemptDto>())!;
        Assert.Equal(AttemptStatuses.Graded, after.Status); // the late save submitted it, and with no essay it was graded at once
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.NotNull((await db.Set<QuizAttempt>().SingleAsync(a => a.Id == attempt.Id)).SubmittedAtUtc);
        }

        Assert.Equal(ProblemCodes.attempt_expired, await CodeAsync(await _t.AnswerAsync(w.Student, attempt.Id, question.QuestionId, [], 3)));
    }

    [Fact]
    public async Task The_attempt_ends_at_the_earlier_of_the_time_limit_and_the_close()
    {
        var w = await _t.SetUpAsync("AT5");
        var close = DateTime.UtcNow.AddMinutes(3);
        var quiz = await _t.BuildAsync(w, QuizSettings(timeLimit: 30, closes: close));
        var noLimit = await _t.BuildAsync(w, QuizSettings());

        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        var open = await _t.StartOkAsync(w.Student, noLimit.Id);

        Assert.True(Math.Abs((attempt.EndsAtUtc!.Value - close).TotalSeconds) < 1);
        Assert.InRange(attempt.RemainingSeconds!.Value, 170, 180);
        Assert.Null(open.EndsAtUtc);
        Assert.Null(open.RemainingSeconds);
    }

    [Fact]
    public async Task Autosave_keeps_highest_sequence()
    {
        var w = await _t.SetUpAsync("AT6");
        var quiz = await _t.BuildAsync(w);
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        var q = attempt.Questions.First(x => x.Type == QuestionTypes.SingleChoice);
        var (a, b) = (q.Options[0].Id, q.Options[1].Id);

        await _t.AnswerAsync(w.Student, attempt.Id, q.QuestionId, [a], 5);
        var stale = (await (await _t.AnswerAsync(w.Student, attempt.Id, q.QuestionId, [b], 3)).Content.ReadFromJsonAsync<SaveAnswerResultDto>())!;
        var newer = (await (await _t.AnswerAsync(w.Student, attempt.Id, q.QuestionId, [b], 6)).Content.ReadFromJsonAsync<SaveAnswerResultDto>())!;
        var racing = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => _t.AnswerAsync(w.Student, attempt.Id, q.QuestionId, [i % 2 == 0 ? a : b], 100 + i)));

        Assert.Equal(5, stale.StoredSequence); // arrived later but lower: ignored
        Assert.Equal(6, newer.StoredSequence);
        Assert.All(racing, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var resumed = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttempt(attempt.Id))).Content.ReadFromJsonAsync<AttemptDto>())!;
        var saved = resumed.Answers.Single(x => x.QuestionId == q.QuestionId);
        Assert.Equal(108, saved.Sequence); // the highest sequence won whatever the arrival order
        Assert.Equal([a], saved.SelectedOptionIds); // 108 is even: option a
    }

    [Fact]
    public async Task Autosave_rejects_options_of_other_questions_double_picks_and_foreign_questions()
    {
        var w = await _t.SetUpAsync("AT7");
        var quiz = await _t.BuildAsync(w, null, Single("S"), Essay("E"));
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        var single = attempt.Questions.First(x => x.Type == QuestionTypes.SingleChoice);
        var essay = attempt.Questions.First(x => x.Type == QuestionTypes.Essay);
        var foreignOption = (await _t.BuildAsync(w)).Question(0).Options[0].Id;

        var other = await _t.AnswerAsync(w.Student, attempt.Id, single.QuestionId, [foreignOption], 1);
        var two = await _t.AnswerAsync(w.Student, attempt.Id, single.QuestionId, [single.Options[0].Id, single.Options[1].Id], 1);
        var unknown = await _t.AnswerAsync(w.Student, attempt.Id, Guid.NewGuid(), [], 1);
        var longEssay = await _t.AnswerAsync(w.Student, attempt.Id, essay.QuestionId, [], 1, new string('x', 20_001));
        var essayWithOptions = await _t.AnswerAsync(w.Student, attempt.Id, essay.QuestionId, [single.Options[0].Id], 1, "t");
        var essayOk = await _t.AnswerAsync(w.Student, attempt.Id, essay.QuestionId, [], 2, new string('x', 20_000));

        foreach (var bad in new[] { other, two, unknown, longEssay, essayWithOptions })
        {
            Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(bad));
        }

        Assert.Equal(HttpStatusCode.OK, essayOk.StatusCode);
    }

    [Fact]
    public async Task Objective_questions_are_auto_graded_all_or_nothing()
    {
        var w = await _t.SetUpAsync("AT8");
        var quiz = await _t.BuildAsync(w); // single 2, multiple 3 (all-or-nothing), true / false 1: total 6

        var perfect = await _t.TakeAsync(w.Student, quiz);
        var second = await _t.StartOkAsync(w.Student, quiz.Id);
        foreach (var asked in second.Questions)
        {
            var index = quiz.Questions.ToList().FindIndex(q => q.Id == asked.QuestionId);
            // Multiple choice with only one of its two correct options: nothing. The others right.
            IReadOnlyList<Guid> pick = quiz.Questions[index].Type == QuestionTypes.MultipleChoice ? [quiz.Correct(index)[0]] : quiz.Correct(index);
            await _t.AnswerAsync(w.Student, second.Id, asked.QuestionId, pick, 1);
        }

        var partial = await _t.SubmitOkAsync(w.Student, second.Id);

        Assert.Equal((6m, 6m, AttemptStatuses.Graded), (perfect.Score, perfect.MaxScore, perfect.Status));
        Assert.Equal((3m, 6m), (partial.Score, partial.MaxScore)); // 2 + 0 + 1
        Assert.False(partial.HasPendingEssays);
        var again = await _t.SubmitOkAsync(w.Student, second.Id); // idempotent
        Assert.Equal(partial.Score, again.Score);
        Assert.Equal(2, await AttemptRowsAsync(quiz.Id));
    }

    [Fact]
    public async Task A_pool_draws_the_set_number_of_questions_and_the_max_score_follows_the_draw()
    {
        var w = await _t.SetUpAsync("AT9");
        var plain = await _t.BuildAsync(w, null, Single("a", 1), Single("b", 1), Single("c", 1), Single("d", 1));
        await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(plain.Id),
            new UpdateQuizRequest(ModuleSettings(), null, null, QuizSettings(draw: 2, review: ReviewPolicies.ScoreOnly, shuffleQuestions: true, shuffleOptions: true)));

        var attempt = await _t.StartOkAsync(w.Student, plain.Id);
        var result = await _t.SubmitOkAsync(w.Student, attempt.Id);

        Assert.Equal(2, attempt.Questions.Count);
        Assert.Equal(2m, result.MaxScore);
        Assert.Equal(2, attempt.Questions.Select(q => q.QuestionId).Distinct().Count());
    }
}
