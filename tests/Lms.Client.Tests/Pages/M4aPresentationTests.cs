using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Lms.Client.Pages.Content;
using Lms.Client.Pages.Forums;
using Lms.Client.Pages.Library;
using Lms.Client.Pages.Teaching;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages;

/// <summary>
/// #517: presentation of the M4a pages in the design system: one h1 per page (PageHeader), invariant dates, the current course tab marked for assistive tech, and empty states that say what to do next.
/// </summary>
public sealed partial class M4aPresentationTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private readonly FakeForumsApi _forum = new();
    private readonly FakeLibraryApi _library = new();

    public M4aPresentationTests()
    {
        Services.AddSingleton<IForumsApi>(_forum);
        Services.AddSingleton<ILibraryApi>(_library);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Services.AddSingleton<ICoursesApi, FakeCoursesApi>();
        Services.AddSingleton<IModulesApi>(new FakeModulesApi { AsStudent = true });
    }

    private async Task OpenAsync(string culture, params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions);
        RenderProviders();
    }

    private static void AssertSingleH1<T>(IRenderedComponent<T> page, string text) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var h1 = Assert.Single(page.FindAll("h1"));
        Assert.Equal(text, h1.TextContent.Trim());
    }

    [Fact]
    public async Task Forum_has_one_h1_and_an_empty_state_with_a_next_step()
    {
        await OpenAsync("en", Permissions.Forums.View, Permissions.Forums.Post);
        var page = Render<ForumPage>(p => p.Add(x => x.Id, Cc));

        var empty = page.WaitForElement("[data-testid=forum-empty]", Wait);
        AssertSingleH1(page, "Forum");
        Assert.Contains("lms-empty", empty.ClassName);
        Assert.Contains("Start the first thread", empty.TextContent);
    }

    [Fact]
    public async Task Thread_title_is_the_page_h1()
    {
        var thread = _forum.AddThread(Cc, "Homework question");
        await OpenAsync("en", Permissions.Forums.View);
        var page = Render<ThreadPage>(p => p.Add(x => x.Id, Cc).Add(x => x.ThreadId, thread.Id));

        page.WaitForElement("[data-testid=thread-title]", Wait);
        AssertSingleH1(page, "Homework question");
        Assert.Equal("H1", page.Find("[data-testid=thread-title]").TagName);
    }

    [Fact]
    public async Task Announcements_have_one_h1_and_invariant_dates_in_arabic()
    {
        _forum.Announcements.Add(new(Guid.NewGuid(), Cc, "Welcome", "<p>Hi</p>", false, Guid.NewGuid(), "معلّم", "Teacher",
            new DateTime(2026, 11, 3, 9, 30, 0, DateTimeKind.Utc), null));
        await OpenAsync("ar", Permissions.Forums.View);
        var page = Render<AnnouncementsPage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement("[data-testid=announcements-list]", Wait);
        AssertSingleH1(page, "الإعلانات");
        var date = page.Find("[data-testid^=announcement-date-]");
        Assert.Equal("ltr", date.GetAttribute("dir"));
        Assert.Matches(IsoDateTime(), date.TextContent.Trim());
    }

    [Fact]
    public async Task Announcements_empty_state_says_what_comes_next()
    {
        await OpenAsync("en", Permissions.Forums.View);
        var page = Render<AnnouncementsPage>(p => p.Add(x => x.Id, Cc));

        var empty = page.WaitForElement("[data-testid=announcements-empty]", Wait);
        Assert.Contains("lms-empty", empty.ClassName);
        Assert.Contains("Your teacher's announcements will appear here.", empty.TextContent);
    }

    [Fact]
    public async Task Course_tabs_mark_the_current_page()
    {
        await OpenAsync("en", Permissions.Forums.View, Permissions.Modules.Manage); // Plan needs Manage and teaching (the fake caller teaches Cc)
        var page = Render<ForumPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=forum-empty]", Wait);

        Assert.Equal("page", page.Find("[data-testid=tab-forum]").GetAttribute("aria-current"));
        Assert.Null(page.Find("[data-testid=tab-plan]").GetAttribute("aria-current"));
        Assert.Null(page.Find("[data-testid=tab-announcements]").GetAttribute("aria-current"));
    }

    [Fact]
    public async Task Library_has_one_h1_and_an_empty_state_with_a_next_step()
    {
        await OpenAsync("en", Permissions.Library.View);
        var page = Render<LibraryPage>();

        var empty = page.WaitForElement("[data-testid=library-empty]", Wait);
        AssertSingleH1(page, "Digital library");
        Assert.Contains("lms-empty", empty.ClassName);
        Assert.Contains("Change the filters", empty.TextContent);
    }

    [Fact]
    public async Task My_course_title_is_the_h1()
    {
        await OpenAsync("ar", Permissions.Modules.View);
        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement("[data-testid=learn-header]", Wait);
        Assert.Single(page.FindAll("h1"));
    }

    [Fact]
    public async Task My_class_courses_has_one_h1_and_soft_plan_version_chips()
    {
        await OpenAsync("en", Permissions.LessonPlans.View);
        var page = Render<MyClassCoursesPage>();

        page.WaitForElement($"[data-testid=my-class-course-{Cc}]", Wait);
        AssertSingleH1(page, "My class courses");
        var chip = page.Find($"[data-testid=my-class-course-{Cc}] [data-testid=plan-version]");
        Assert.Contains("mud-chip-text", chip.ClassName);
    }

    [Fact]
    public async Task Forum_threads_are_rows_of_one_card()
    {
        _forum.AddThread(Cc, "First");
        _forum.AddThread(Cc, "Second");
        await OpenAsync("en", Permissions.Forums.View);
        var page = Render<ForumPage>(p => p.Add(x => x.Id, Cc));

        var list = page.WaitForElement("[data-testid=forum-threads]", Wait);
        Assert.Equal("UL", list.TagName);
        Assert.Equal(2, list.QuerySelectorAll("li.lms-item").Length);
        Assert.Single(page.FindAll("[data-testid=forum-threads-card]"));
    }

    [Fact]
    public async Task Announcements_are_articles_in_one_card_with_their_own_heading()
    {
        _forum.Announcements.Add(new(Guid.NewGuid(), Cc, "Welcome", "<p>Hi</p>", false, Guid.NewGuid(), "م", "Teacher", DateTime.UtcNow, null));
        _forum.Announcements.Add(new(Guid.NewGuid(), Cc, "Exam moved", "<p>Friday</p>", true, Guid.NewGuid(), "م", "Teacher", DateTime.UtcNow, null));
        await OpenAsync("en", Permissions.Forums.View);
        var page = Render<AnnouncementsPage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement("[data-testid=announcements-list]", Wait);
        var articles = page.FindAll("[data-testid=announcements-card] article");
        Assert.Equal(2, articles.Count);
        Assert.All(articles, a => Assert.NotNull(a.QuerySelector($"h2#{a.GetAttribute("aria-labelledby")}")));
    }

    [Fact]
    public async Task Library_card_actions_are_labelled_icon_buttons_with_delete_last()
    {
        var own = _library.Add("Atlas");
        await OpenAsync("en", Permissions.Library.View, Permissions.Library.Manage, Permissions.Modules.Manage);
        var page = Render<LibraryPage>();

        var card = page.WaitForElement($"[data-testid=library-card-{own.Id}]", Wait);
        var buttons = card.QuerySelectorAll(".lms-row-actions button");
        Assert.Equal(new[] { "Add to lesson", "Edit", "Delete" }, buttons.Select(b => b.GetAttribute("aria-label") ?? "").ToArray());
        Assert.Equal($"library-delete-{own.Id}", buttons.Last().GetAttribute("data-testid"));
        Assert.NotNull(page.Find("[data-testid=library-filters][role=search]"));
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$")]
    private static partial Regex IsoDateTime();
}
