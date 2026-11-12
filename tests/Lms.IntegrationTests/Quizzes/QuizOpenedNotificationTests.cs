using System.Net;
using Lms.Api.Features.Quizzes;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Notifications;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Quizzes.AttemptTestKit;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>M4a wave 2: the Quizzes.Opened event reaches the targeted students once, when the quiz is open and takeable.</summary>
[Collection(ApiCollection.Name)]
public sealed class QuizOpenedNotificationTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<List<Notification>> OpenedAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().Where(n => n.UserId == userId && n.EventKey == NotificationEvents.QuizzesOpened).ToListAsync();
    }

    private async Task SweepAsync()
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<QuizCloseJob>().RunAsync();
    }

    [Fact]
    public async Task A_quiz_published_already_open_notifies_the_enrolled_students_once_at_its_first_question()
    {
        var w = await _t.SetUpAsync("QN1");
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);

        var quiz = await _t.CreateQuizAsync(w.Teacher, w, title: "Unit test");
        var before = await OpenedAsync(w.Student.UserId); // no question yet: nothing to take
        await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single());
        await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("second"));
        await SweepAsync();

        Assert.Empty(before);
        var mine = Assert.Single(await OpenedAsync(w.Student.UserId));
        Assert.Single(await OpenedAsync(other.Student.UserId));
        Assert.Empty(await OpenedAsync(w.Teacher.UserId)); // the author is not told
        Assert.Contains("Unit test", mine.ParametersJson);
        Assert.Equal($"/class-courses/{w.ClassCourse.Id}/modules/{quiz.Module.Id}", mine.DeepLink); // the student module viewer
    }

    [Fact]
    public async Task A_scheduled_open_time_or_a_draft_notifies_when_it_arrives_and_never_twice()
    {
        var w = await _t.SetUpAsync("QN2");
        var later = await _t.CreateQuizAsync(w.Teacher, w, QuizSettings(opens: DateTime.UtcNow.AddHours(2))); // created with its open time
        await _t.AddQuestionAsync(w.Teacher, later.Id, Single());
        var draft = await _t.CreateQuizAsync(w.Teacher, w, visibility: ModuleVisibilities.Hidden);
        await _t.AddQuestionAsync(w.Teacher, draft.Id, Single());

        await SweepAsync();
        var waiting = await OpenedAsync(w.Student.UserId);
        await SetQuizWindowAsync(factory, later.Id, DateTime.UtcNow.AddMinutes(-1), null); // the open time arrives
        var publish = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(draft.Id),
            new UpdateQuizRequest(ModuleSettings(), null, null, QuizSettings())); // the draft is published
        await SweepAsync();
        await SweepAsync();

        Assert.Empty(waiting);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(2, (await OpenedAsync(w.Student.UserId)).Count); // one per quiz, not per sweep
    }

    [Fact]
    public async Task A_group_target_notifies_only_the_members_of_the_group()
    {
        var w = await _t.SetUpAsync("QN3");
        var outsider = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var group = await _t.AddGroupAsync(w.ClassCourse, w.StudentProfileId);
        var quiz = await _t.CreateQuizAsync(w.Teacher, w, visibility: ModuleVisibilities.Hidden);
        await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single());

        var module = ModuleSettings() with { Target = ModuleTargets.Group, GroupId = group.Id };
        var response = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id), new UpdateQuizRequest(module, null, null, QuizSettings()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await OpenedAsync(w.Student.UserId));
        Assert.Empty(await OpenedAsync(outsider.Student.UserId));
    }
}
