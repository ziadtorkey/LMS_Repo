using System.Globalization;
using Bunit;
using Lms.Client.Pages;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages;

public sealed class HomeActivityCardsTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private readonly FakeActivityOverviewApi _activities = new();

    public HomeActivityCardsTests()
    {
        Services.AddSingleton<IActivityOverviewApi>(_activities);
        Culture.Current = new CultureInfo("en");
    }

    private static CourseAssignmentListItemDto Assignment(string title, string? myStatus, DateTime? due = null, int? submitted = null, int? graded = null, bool visible = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), title + " ar", title, "Published", visible, due, 10, submitted, graded, myStatus, null);

    private static QuizListItemDto Quiz(string title, DateTime? opens, DateTime? closes, bool visible = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), title + " ar", title, "Published", visible, opens, closes, 5, null, 0, null);

    private async Task<IRenderedComponent<Home>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<Home>();
        page.WaitForElement("p, .mud-typography-body1", Wait);
        return page;
    }

    [Fact]
    public async Task Home_cards_depend_on_permissions_student_sees_due_and_open_cards()
    {
        var now = DateTime.UtcNow;
        var todo = Assignment("Essay", null, now.AddDays(2));
        _activities.Assignments[Cc] = [todo, Assignment("Handed in", "Submitted", now.AddDays(1)), Assignment("Hidden", null, visible: false)];
        var open = Quiz("Unit quiz", now.AddHours(-1), now.AddHours(1));
        _activities.Quizzes[Cc] = [open, Quiz("Later", now.AddDays(1), now.AddDays(2)), Quiz("Closed", now.AddDays(-2), now.AddDays(-1))];

        var page = await OpenAsync(Permissions.CourseAssignments.View, Permissions.Quizzes.View, Permissions.Quizzes.Attempt, Permissions.LessonPlans.View);

        page.WaitForElement("[data-testid=home-due-assignments]", Wait);
        Assert.Single(page.FindAll("[data-testid^=home-due-]:not([data-testid=home-due-assignments]):not([data-testid=home-due-empty])"));
        Assert.Contains($"modules/{todo.ModuleId}", page.Find($"[data-testid=home-due-{todo.Id}]").GetAttribute("href"));
        Assert.Single(page.FindAll("[data-testid^=home-quiz-]"));
        Assert.Contains($"modules/{open.ModuleId}", page.Find($"[data-testid=home-quiz-{open.Id}]").GetAttribute("href"));
        Assert.Empty(page.FindAll("[data-testid=home-to-grade]"));
    }

    [Fact]
    public async Task Home_cards_depend_on_permissions_grader_sees_only_the_grading_card()
    {
        var waiting = Assignment("Report", null, submitted: 5, graded: 2);
        _activities.Assignments[Cc] = [waiting, Assignment("All graded", null, submitted: 3, graded: 3)];

        var page = await OpenAsync(Permissions.CourseAssignments.View, Permissions.CourseAssignments.Manage, Permissions.CourseAssignments.Grade, Permissions.LessonPlans.View);

        page.WaitForElement("[data-testid=home-to-grade]", Wait);
        Assert.Single(page.FindAll("[data-testid^=home-grade-]:not([data-testid=home-grade-empty])"));
        Assert.Contains("3 waiting", page.Find("[data-testid=home-to-grade]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=home-due-assignments]"));
        Assert.Empty(page.FindAll("[data-testid=home-open-quizzes]"));
    }

    [Fact]
    public async Task Home_cards_depend_on_permissions_none_without_permissions()
    {
        var page = await OpenAsync(Permissions.LessonPlans.View);

        Assert.Empty(page.FindAll("[data-testid=home-activity-cards]"));
    }

    [Fact]
    public async Task Empty_lists_say_so()
    {
        var page = await OpenAsync(Permissions.CourseAssignments.View, Permissions.Quizzes.View, Permissions.Quizzes.Attempt, Permissions.LessonPlans.View);

        page.WaitForElement("[data-testid=home-due-empty]", Wait);
        Assert.NotNull(page.Find("[data-testid=home-quizzes-empty]"));
    }
}
