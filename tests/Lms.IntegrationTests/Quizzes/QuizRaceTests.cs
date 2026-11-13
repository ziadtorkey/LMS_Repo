using System.Net;
using Lms.Api.Features.Quizzes;
using Lms.Domain.Grading;
using Lms.Domain.Notifications;
using Lms.Domain.Quizzes;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Notifications;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Quizzes.AttemptTestKit;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>Races that must stay harmless: a double submit, the sweep against a student submit, and concurrent opened notifications.</summary>
[Collection(ApiCollection.Name)]
public sealed class QuizRaceTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<int> EventsAsync(Guid quizId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<GradeEvent>().CountAsync(e => e.SourceKind == GradeSourceKind.Quiz && e.SourceId == quizId);
    }

    private async Task SweepAsync()
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<QuizCloseJob>().RunAsync();
    }

    [Fact]
    public async Task A_double_submit_grades_and_records_the_grade_once()
    {
        var w = await _t.SetUpAsync("QR1");
        var quiz = await _t.BuildAsync(w);
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        await _t.AnswerAsync(w.Student, attempt.Id, attempt.Questions[0].QuestionId, [attempt.Questions[0].Options[0].Id], 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _t.SendAsync(w.Student, HttpMethod.Post, Lms.Shared.Routes.QuizAttemptSubmit(attempt.Id))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await EventsAsync(quiz.Id));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(AttemptStatuses.Graded, (await db.Set<QuizAttempt>().SingleAsync(a => a.Id == attempt.Id)).Status);
    }

    [Fact]
    public async Task The_sweep_racing_a_student_submit_submits_once()
    {
        var w = await _t.SetUpAsync("QR2");
        var quiz = await _t.BuildAsync(w, QuizSettings(timeLimit: 30));
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        await _t.AnswerAsync(w.Student, attempt.Id, attempt.Questions[0].QuestionId, [attempt.Questions[0].Options[0].Id], 1);
        await SetEndAsync(factory, attempt.Id, DateTime.UtcNow.AddMinutes(-3)); // due for the sweep

        var work = new List<Task>
        {
            SweepAsync(), SweepAsync(),
            _t.SendAsync(w.Student, HttpMethod.Post, Lms.Shared.Routes.QuizAttemptSubmit(attempt.Id)),
            _t.SendAsync(w.Student, HttpMethod.Post, Lms.Shared.Routes.QuizAttemptSubmit(attempt.Id)),
        };
        await Task.WhenAll(work);

        Assert.Equal(1, await EventsAsync(quiz.Id)); // graded and recorded exactly once, whoever won
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.Set<QuizAttempt>().SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatuses.Graded, row.Status);
        Assert.NotNull(row.SubmittedAtUtc);
    }

    [Fact]
    public async Task Concurrent_triggers_send_each_student_one_opened_notification()
    {
        var w = await _t.SetUpAsync("QR3");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);
        await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single()); // first question: sent once

        await Task.WhenAll(
            Enumerable.Range(0, 5).Select(i => (Task)_t.AddQuestionAsync(w.Teacher, quiz.Id, Single("q" + i)))
                .Concat(Enumerable.Range(0, 5).Select(_ => SweepAsync())));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<Notification>().CountAsync(n => n.UserId == w.Student.UserId && n.EventKey == NotificationEvents.QuizzesOpened));
    }
}
