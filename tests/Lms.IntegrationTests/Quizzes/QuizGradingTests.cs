using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Content;
using Lms.Domain.Grading;
using Lms.Domain.Quizzes;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;
using static Lms.IntegrationTests.Quizzes.AttemptTestKit;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>M4a Task 11 (FR-QZ-02, FR-QZ-03): essays by hand, the grading methods, grade events and completion.</summary>
[Collection(ApiCollection.Name)]
public sealed class QuizGradingTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<List<GradeEvent>> EventsAsync(Guid quizId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<GradeEvent>().Where(e => e.SourceKind == GradeSourceKind.Quiz && e.SourceId == quizId).OrderBy(e => e.Revision).ToListAsync();
    }

    private Task<HttpResponseMessage> GradeAsync(Lms.Domain.Access.RoleAssignment who, Guid attemptId, Guid questionId, decimal points, string? feedback = null) =>
        _t.SendAsync(who, HttpMethod.Put, Routes.QuizAttemptGrade(attemptId), new GradeAttemptRequest([new EssayGradeInput(questionId, points, feedback)]));

    [Fact]
    public async Task Essay_pending_until_teacher_grades()
    {
        var w = await _t.SetUpAsync("QG1");
        var quiz = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.Answers), Single("S", 2, 0), Essay("E", 5));

        var submitted = await _t.TakeAsync(w.Student, quiz);
        var attempt = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.QuizAttemptsOf(quiz.Id))).Content
            .ReadFromJsonAsync<PagedResult<AttemptSummaryDto>>())!.Items.Single();

        Assert.Equal(AttemptStatuses.Submitted, submitted.Status);
        Assert.True(submitted.HasPendingEssays);
        Assert.Null(submitted.Score); // no score yet
        Assert.True(attempt.HasPendingEssays);
        var essay = quiz.Questions.First(q => q.Type == QuestionTypes.Essay);
        Assert.Empty(await EventsAsync(quiz.Id)); // nothing for the gradebook while an essay is pending

        var tooHigh = await GradeAsync(w.Teacher, attempt.Id, essay.Id, 5.5m);
        var objective = await GradeAsync(w.Teacher, attempt.Id, quiz.Questions.First(q => q.Type != QuestionTypes.Essay).Id, 1);
        var student = await GradeAsync(w.Student, attempt.Id, essay.Id, 1);
        var other = await GradeAsync(w.OtherTeacher, attempt.Id, essay.Id, 1);
        var ok = await GradeAsync(w.Teacher, attempt.Id, essay.Id, 3.5m, "Good");

        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(tooHigh));
        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(objective));
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        var graded = (await ok.Content.ReadFromJsonAsync<AttemptResultDto>())!;
        Assert.Equal((AttemptStatuses.Graded, 5.5m, 7m, false), (graded.Status, graded.Score, graded.MaxScore, graded.HasPendingEssays));
        var mine = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttemptResult(attempt.Id))).Content.ReadFromJsonAsync<AttemptResultDto>())!;
        Assert.Equal(5.5m, mine.Score);
        Assert.Equal("Good", mine.Questions!.Single(q => q.Type == QuestionTypes.Essay).Feedback);
        Assert.Single(await EventsAsync(quiz.Id));
    }

    [Fact]
    public async Task Highest_last_average_methods_decide_the_quiz_score()
    {
        var w = await _t.SetUpAsync("QG2");
        var highest = await _t.BuildAsync(w, QuizSettings(method: GradingMethods.Highest, review: ReviewPolicies.ScoreOnly));
        var last = await _t.BuildAsync(w, QuizSettings(method: GradingMethods.Last, review: ReviewPolicies.ScoreOnly));
        var average = await _t.BuildAsync(w, QuizSettings(method: GradingMethods.Average, review: ReviewPolicies.ScoreOnly));

        foreach (var quiz in new[] { highest, last, average })
        {
            await _t.TakeAsync(w.Student, quiz, right: true); // 100 %
            await _t.TakeAsync(w.Student, quiz, right: false); // 0 %
        }

        async Task<decimal?> FinalAsync(BuiltQuiz quiz) =>
            (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizSummary(quiz.Id))).Content.ReadFromJsonAsync<QuizSummaryDto>())!.FinalScore;

        Assert.Equal(6m, await FinalAsync(highest));
        Assert.Equal(0m, await FinalAsync(last));
        Assert.Equal(3m, await FinalAsync(average));
    }

    [Fact]
    public async Task Grade_event_written_once_per_final_score()
    {
        var w = await _t.SetUpAsync("QG3");
        var quiz = await _t.BuildAsync(w, QuizSettings(method: GradingMethods.Highest, review: ReviewPolicies.ScoreOnly));

        await _t.TakeAsync(w.Student, quiz, right: false); // 0
        var first = await EventsAsync(quiz.Id);
        await _t.TakeAsync(w.Student, quiz, right: false); // still 0: the final score did not change
        var unchanged = await EventsAsync(quiz.Id);
        await _t.TakeAsync(w.Student, quiz, right: true); // 6: a new revision
        var better = await EventsAsync(quiz.Id);

        Assert.Equal([(0m, 6m, 1)], first.Select(e => (e.Points, e.MaxPoints, e.Revision)).ToArray());
        Assert.Single(unchanged);
        Assert.Equal([(0m, 6m, 1), (6m, 6m, 2)], better.Select(e => (e.Points, e.MaxPoints, e.Revision)).ToArray());
        Assert.All(better, e => Assert.Equal((w.StudentProfileId, w.ClassCourse.Id), (e.StudentProfileId, e.ClassCourseId)));
    }

    [Fact]
    public async Task The_score_completion_rule_completes_the_module_at_the_minimum_and_submit_completes_it_on_submit()
    {
        var w = await _t.SetUpAsync("QG4");
        var scoreQuiz = await _t.BuildAsync(w, QuizSettings(maxAttempts: null));
        await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(scoreQuiz.Id), new UpdateQuizRequest(
            ModuleSettings() with { CompletionMinScore = 60 }, null, null, QuizSettings()));
        var submitQuiz = await _t.BuildAsync(w, QuizSettings());
        await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(submitQuiz.Id), new UpdateQuizRequest(
            ModuleSettings(rule: CompletionRules.Submit), null, null, QuizSettings()));

        async Task<string> StateAsync(BuiltQuiz quiz) =>
            (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ModuleCompletion(quiz.Quiz.Module.Id))).Content.ReadFromJsonAsync<ModuleCompletionDto>())!.State;

        await _t.TakeAsync(w.Student, scoreQuiz, right: false); // 0 % < 60
        var low = await StateAsync(scoreQuiz);
        await _t.TakeAsync(w.Student, scoreQuiz, right: true); // 100 %
        var high = await StateAsync(scoreQuiz);
        await _t.TakeAsync(w.Student, submitQuiz, right: false);

        Assert.Equal(CompletionStates.InProgress, low);
        Assert.Equal(CompletionStates.Completed, high);
        Assert.Equal(CompletionStates.Completed, await StateAsync(submitQuiz));
    }

    [Fact]
    public async Task Another_students_attempt_is_404_and_staff_see_everything_with_correct_options()
    {
        var w = await _t.SetUpAsync("QG5");
        var quiz = await _t.BuildAsync(w, QuizSettings(review: ReviewPolicies.None));
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var result = await _t.TakeAsync(w.Student, quiz);

        var peek = await _t.SendAsync(other.Student, HttpMethod.Get, Routes.QuizAttemptResult(result.AttemptId));
        var peekAttempt = await _t.SendAsync(other.Student, HttpMethod.Get, Routes.QuizAttempt(result.AttemptId));
        var peekAnswer = await _t.AnswerAsync(other.Student, result.AttemptId, quiz.Question(0).Id, [], 1);
        var staff = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.QuizAttemptResult(result.AttemptId))).Content.ReadFromJsonAsync<AttemptResultDto>())!;
        var admin = await _t.SendAsync(w.Admin, HttpMethod.Get, Routes.QuizAttemptResult(result.AttemptId));

        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, peekAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, peekAnswer.StatusCode);
        Assert.Equal(6m, staff.Score); // staff see the score whatever the student policy
        Assert.All(staff.Questions!.SelectMany(q => q.Options), o => Assert.NotNull(o.IsCorrect));
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttemptsOf(quiz.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.QuizAttemptsOf(quiz.Id))).StatusCode);
    }
}
