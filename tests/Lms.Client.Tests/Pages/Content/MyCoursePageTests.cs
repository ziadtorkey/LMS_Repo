using System.Globalization;
using Bunit;
using Lms.Client.Pages.Content;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages.Content;

public sealed class MyCoursePageTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid Lesson = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private bool _providers;
    private readonly FakeModulesApi _modules = new() { AsStudent = true };

    public MyCoursePageTests() => Services.AddSingleton<IModulesApi>(_modules);

    private ModuleDetailDto Seed(string title, string rule, string kind = ModuleKinds.Page) =>
        _modules.Seed(Cc, Lesson, new CreateModuleRequest(kind, title + " ar", title, ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, rule, null, new ModuleContentInput(BodyHtmlEn: "<p>x</p>")));

    /// <summary>Puts the fake modules into the student's plan, the way the server nests them for a student.</summary>
    private void PublishPlan()
    {
        var nodes = _modules.NodesFor(Cc, Lesson);
        var counted = nodes.Where(n => n.CompletionRule != CompletionRules.None).ToList();
        var done = counted.Count(n => n.CompletionState == CompletionStates.Completed);
        var plan = Plans.Plans[Cc];
        var sections = plan.Sections.Select(s => s with
        {
            Lessons = s.Lessons.Select(l => l.Key == Lesson ? l with { Modules = nodes, Progress = new ProgressDto(done, counted.Count) } : l with { Modules = [], Progress = new ProgressDto(0, 0) }).ToList(),
        }).ToList();
        Plans.Plans[Cc] = plan with { Sections = sections, Progress = new ProgressDto(done, counted.Count) };
        Plans.HideHiddenItems = true;
    }

    private async Task<IRenderedComponent<MyCoursePage>> OpenAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        if (!_providers)
        {
            await GrantAsync(Permissions.Modules.View);
            RenderProviders();
            _providers = true;
        }
        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=learn-header]", Wait);
        return page;
    }

    [Fact]
    public async Task Continue_opens_next_module()
    {
        var first = Seed("Done first", CompletionRules.Manual);
        await _modules.MarkCompleteAsync(first.Node.Id);
        var next = Seed("Read me next", CompletionRules.Manual);
        PublishPlan();

        var page = await OpenAsync();

        Assert.Contains("Read me next", page.Find("[data-testid=continue-title]").TextContent);
        Assert.Contains($"class-courses/{Cc}/modules/{next.Node.Id}", page.Find("[data-testid=continue-open]").GetAttribute("href"));
        Assert.Contains("1 of 2", page.Find("[data-testid=learn-progress-text]").TextContent);
    }

    [Fact]
    public async Task View_rule_module_shows_done_after_open()
    {
        var viewed = Seed("Watch this", CompletionRules.View, ModuleKinds.Video);
        PublishPlan();
        var page = await OpenAsync();
        Assert.Contains("Not started", page.Find($"[data-testid=learn-module-state-{viewed.Node.Id}]").TextContent);

        await _modules.GetAsync(viewed.Node.Id); // the module viewer opens it: the server counts that as a view
        PublishPlan();
        var reopened = await OpenAsync();

        Assert.Contains("Done", reopened.Find($"[data-testid=learn-module-state-{viewed.Node.Id}]").TextContent);
        Assert.Contains("completed everything", reopened.Find("[data-testid=continue-done]").TextContent);
    }

    [Fact]
    public async Task Submit_rule_module_shows_waiting_state()
    {
        var work = Seed("Homework", CompletionRules.Submit, ModuleKinds.Assignment);
        var quiz = Seed("Quiz 1", CompletionRules.Score, ModuleKinds.Quiz);
        PublishPlan();

        var page = await OpenAsync();

        Assert.Contains("Waiting for your submission", page.Find($"[data-testid=learn-module-state-{work.Node.Id}]").TextContent);
        Assert.Contains("Waiting for a score", page.Find($"[data-testid=learn-module-state-{quiz.Node.Id}]").TextContent);
    }

    [Fact]
    public async Task Untracked_modules_are_listed_without_a_completion_state()
    {
        var info = Seed("Reading list", CompletionRules.None);
        PublishPlan();

        var page = await OpenAsync();

        Assert.Contains("Not tracked", page.Find($"[data-testid=learn-module-state-{info.Node.Id}]").TextContent);
        Assert.Contains("Nothing", page.Find("[data-testid=continue-none]").TextContent);
    }

    [Fact]
    public async Task Lesson_progress_and_arabic_titles_are_shown()
    {
        var a = Seed("A", CompletionRules.Manual);
        await _modules.MarkCompleteAsync(a.Node.Id);
        Seed("B", CompletionRules.Manual);
        PublishPlan();

        var page = await OpenAsync("ar");

        Assert.Contains("1/2", page.Find($"[data-testid=learn-lesson-progress-{Lesson}]").TextContent);
        Assert.Contains("A ar", page.Find($"[data-testid=learn-module-{a.Node.Id}]").TextContent);
    }

    [Fact]
    public async Task A_failed_progress_call_shows_the_error_not_all_done()
    {
        var done = Seed("Only one", CompletionRules.Manual);
        await _modules.MarkCompleteAsync(done.Node.Id);
        PublishPlan();
        _modules.Failure = new ApiProblemException(500, "server_error", null, null);
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Modules.View);
        RenderProviders();

        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement("[data-testid=learn-error]", Wait);
        Assert.Empty(page.FindAll("[data-testid=continue-done]"));
    }

    [Fact]
    public async Task Forum_and_announcement_links_need_Forums_View()
    {
        PublishPlan();
        await GrantAsync(Permissions.Modules.View, "Forums.View");
        RenderProviders();
        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=learn-header]", Wait);

        Assert.EndsWith($"class-courses/{Cc}/forum", page.Find("[data-testid=learn-forum]").GetAttribute("href"));
        Assert.EndsWith($"class-courses/{Cc}/announcements", page.Find("[data-testid=learn-announcements]").GetAttribute("href"));
    }

    [Fact]
    public async Task No_forum_links_without_Forums_View()
    {
        PublishPlan();
        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid=learn-course-links]"));
    }

    [Fact]
    public async Task Without_Modules_View_the_page_is_forbidden()
    {
        await GrantAsync(Permissions.LessonPlans.View);
        RenderProviders();
        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement("[data-testid=learn-forbidden]", Wait);
    }
}
