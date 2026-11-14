using System.Net;
using System.Net.Http.Json;
using Lms.Api.Features.Quizzes;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Quizzes.AttemptTestKit;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>M4a Task 11 (FR-QZ-02): auto-submit at the end of the time, by the sweep and by the Development trigger.</summary>
[Collection(ApiCollection.Name)]
public sealed class AutoSubmitTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<int> SweepAsync()
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.Empty, true);
        return await scope.ServiceProvider.GetRequiredService<QuizCloseJob>().RunAsync();
    }

    private async Task<QuizAttempt> AttemptAsync(Guid attemptId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<QuizAttempt>().AsNoTracking().SingleAsync(a => a.Id == attemptId);
    }

    [Fact]
    public async Task Unfinished_attempts_auto_submitted_at_close()
    {
        var w = await _t.SetUpAsync("AS1");
        var quiz = await _t.BuildAsync(w, QuizSettings(timeLimit: 30));
        var late = await _t.StartOkAsync(w.Student, quiz.Id);
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var onTime = await _t.StartOkAsync(other.Student, quiz.Id);
        var inGrace = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var graceAttempt = await _t.StartOkAsync(inGrace.Student, quiz.Id);
        await _t.AnswerAsync(w.Student, late.Id, late.Questions[0].QuestionId, [late.Questions[0].Options[0].Id], 1);
        await SetEndAsync(factory, late.Id, DateTime.UtcNow.AddMinutes(-3)); // past end + grace
        await SetEndAsync(factory, graceAttempt.Id, DateTime.UtcNow.AddSeconds(-5)); // inside the grace: still taken

        var submitted = await SweepAsync();
        var again = await SweepAsync(); // idempotent

        Assert.True(submitted >= 1);
        Assert.Equal(0, again);
        var done = await AttemptAsync(late.Id);
        Assert.Equal((AttemptStatuses.Graded, true), (done.Status, done.SubmittedAtUtc is not null));
        Assert.Equal(AttemptStatuses.InProgress, (await AttemptAsync(onTime.Id)).Status);
        Assert.Equal(AttemptStatuses.InProgress, (await AttemptAsync(graceAttempt.Id)).Status);
    }

    [Fact]
    public async Task A_quiz_closed_by_an_edit_submits_attempts_that_had_no_end_of_their_own()
    {
        var w = await _t.SetUpAsync("AS2");
        var quiz = await _t.BuildAsync(w, QuizSettings()); // no limit, no close: the attempt has no EndsAtUtc
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        Assert.Null(attempt.EndsAtUtc);
        await SetQuizWindowAsync(factory, quiz.Id, null, DateTime.UtcNow.AddMinutes(-5)); // the teacher closed it

        await SweepAsync();

        Assert.Equal(AttemptStatuses.Graded, (await AttemptAsync(attempt.Id)).Status);
    }

    [Fact]
    public async Task An_essay_attempt_stays_pending_after_an_auto_submit()
    {
        var w = await _t.SetUpAsync("AS3");
        var quiz = await _t.BuildAsync(w, QuizSettings(timeLimit: 5), Single("S"), Essay("E"));
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        await SetEndAsync(factory, attempt.Id, DateTime.UtcNow.AddMinutes(-2));

        await SweepAsync();

        var row = await AttemptAsync(attempt.Id);
        Assert.Equal((AttemptStatuses.AutoSubmitted, true), (row.Status, row.HasPendingEssays));
    }

    [Fact]
    public async Task Dev_quiz_close_trigger_auto_submits_in_development()
    {
        var w = await _t.SetUpAsync("AS4");
        var quiz = await _t.BuildAsync(w, QuizSettings(timeLimit: 30));
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);
        await SetEndAsync(factory, attempt.Id, DateTime.UtcNow.AddSeconds(-2)); // ended, inside the grace: the trigger ignores the grace
        var url = $"/api/dev/jobs/quiz-close/{quiz.Id}";

        var student = await _t.SendAsync(w.Student, HttpMethod.Post, url);
        var other = await _t.SendAsync(w.OtherTeacher, HttpMethod.Post, url);
        var unknown = await _t.SendAsync(w.Teacher, HttpMethod.Post, $"/api/dev/jobs/quiz-close/{Guid.NewGuid()}");
        var ok = await _t.SendAsync(w.Teacher, HttpMethod.Post, url);

        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("\"submitted\":1", await ok.Content.ReadAsStringAsync());
        Assert.Equal(AttemptStatuses.Graded, (await AttemptAsync(attempt.Id)).Status); // the response came back after the work was done
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void Dev_quiz_close_trigger_is_mapped_only_in_development(string environment, bool mapped)
    {
        // The gate itself: a bare host in the given environment (a full Production host needs key-protection certificates).
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(
            new Microsoft.AspNetCore.Builder.WebApplicationOptions { EnvironmentName = environment });
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication();
        builder.Services.AddScoped<LmsDbContext>(_ => null!); // only so the handler parameters can be inferred; nothing is called
        builder.Services.AddScoped<QuizCloseJob>(_ => null!);
        using var app = builder.Build();

        DevQuizCloseTrigger.Map(app);

        var patterns = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToList();
        Assert.Equal(mapped, patterns.Contains(DevQuizCloseTrigger.Pattern));
    }
}
