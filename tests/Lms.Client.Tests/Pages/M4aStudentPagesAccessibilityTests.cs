using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lms.Client.Pages;
using Lms.Client.Pages.Content;
using Lms.Client.Pages.Forums;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages;

/// <summary>
/// M4a accessibility sweep (T28, #378) of the learning-side pages that are on main, in Arabic and English. Structural rules: every
/// control has an accessible name, every field a label, images have alt text, iframes are titled. The module viewer and the student
/// course home must also have exactly one h1; the other pages' headings are restyled by #527 and #506. (Dialogs, quiz and assignment pages are covered elsewhere.)
/// </summary>
public sealed class M4aStudentPagesAccessibilityTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid Lesson = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private readonly FakeForumsApi _forum = new();
    private readonly FakeModulesApi _modules = new() { AsStudent = true, CanEdit = false };
    private readonly FakeActivityOverviewApi _activities = new();

    public M4aStudentPagesAccessibilityTests()
    {
        Services.AddSingleton<IForumsApi>(_forum);
        Services.AddSingleton<IModulesApi>(_modules);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Services.AddSingleton<IActivityOverviewApi>(_activities);
    }

    public static IEnumerable<object[]> Cultures => [["en"], ["ar"]];

    private static string Name(IElement e)
    {
        var label = e.GetAttribute("aria-label") ?? e.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(label)) return label;
        var labelledBy = e.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy) && e.Owner?.GetElementById(labelledBy) is { } target && !string.IsNullOrWhiteSpace(target.TextContent)) return target.TextContent;
        return e.TextContent.Trim();
    }

    private static IDocument Parse(string markup) => new AngleSharp.Html.Parser.HtmlParser().ParseDocument("<html><body>" + markup + "</body></html>");

    /// <summary>The structural rules; returns what breaks them.</summary>
    internal static List<string> Audit(IDocument document, bool requireOneH1 = true)
    {
        var problems = new List<string>();
        foreach (var b in document.QuerySelectorAll("button, a[href], [role=button]"))
            if (string.IsNullOrWhiteSpace(Name(b)) && b.QuerySelector("img[alt]:not([alt=''])") is null)
                problems.Add($"control without a name: {b.OuterHtml[..Math.Min(140, b.OuterHtml.Length)]}");
        foreach (var img in document.QuerySelectorAll("img"))
            if (img.GetAttribute("alt") is null) problems.Add($"image without alt: {img.OuterHtml[..Math.Min(100, img.OuterHtml.Length)]}");
        foreach (var i in document.QuerySelectorAll("input:not([type=hidden]):not([type=file]), textarea, select"))
        {
            var id = i.GetAttribute("id");
            var labelled = !string.IsNullOrWhiteSpace(i.GetAttribute("aria-label")) || !string.IsNullOrWhiteSpace(i.GetAttribute("aria-labelledby"))
                || (id is not null && document.QuerySelector($"label[for='{id}']") is not null) || i.Closest("label") is not null
                || i.Closest(".mud-input-control")?.QuerySelector("label, .mud-input-label") is not null
                || i.Closest(".mud-radio, .mud-checkbox, .mud-switch") is not null;
            if (!labelled) problems.Add($"field without a label: {i.OuterHtml[..Math.Min(400, i.OuterHtml.Length)]}");
        }
        foreach (var f in document.QuerySelectorAll("input[type=file]"))
            if (string.IsNullOrWhiteSpace(f.GetAttribute("aria-label"))) problems.Add("file input without aria-label");
        foreach (var iframe in document.QuerySelectorAll("iframe"))
            if (string.IsNullOrWhiteSpace(iframe.GetAttribute("title"))) problems.Add("iframe without a title");
        foreach (var m in document.QuerySelectorAll("[role=progressbar]"))
            if (string.IsNullOrWhiteSpace(Name(m)) && string.IsNullOrWhiteSpace(m.GetAttribute("aria-labelledby"))) problems.Add($"progressbar without a name: {m.OuterHtml[..Math.Min(120, m.OuterHtml.Length)]}");
        var h1 = document.QuerySelectorAll("h1").Length;
        if (requireOneH1 && h1 != 1) problems.Add($"expected exactly one h1, found {h1}");
        return problems;
    }

    private static void AssertClean(string markup, string page, bool requireOneH1 = true)
    {
        var problems = Audit(Parse(markup), requireOneH1).Distinct().ToList();
        Assert.True(problems.Count == 0, $"{page}: " + string.Join("\n", problems));
    }

    private async Task StartAsync(string culture, params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        RenderProviders();
    }

    private ModuleDetailDto Seed(string title, string kind, ModuleContentInput? content = null, AssetDto? asset = null, string rule = CompletionRules.None) =>
        _modules.Seed(Cc, Lesson, new CreateModuleRequest(kind, title + " ar", title, ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, rule, null, content ?? new ModuleContentInput()), asset);

    [Theory, MemberData(nameof(Cultures))]
    public async Task Forum_and_thread_pages_are_accessible(string culture)
    {
        _forum.AddThread(Cc, "Homework help", pinned: true);
        var locked = _forum.AddThread(Cc, "Closed thread", locked: true);
        await StartAsync(culture, Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate, Permissions.LessonPlans.View);

        var list = Render<ForumPage>(p => p.Add(x => x.Id, Cc));
        list.WaitForElement("[data-testid=forum-threads]", Wait);
        AssertClean(list.Markup, "forum list", requireOneH1: false);   // h1: the designer's #527

        var thread = Render<ThreadPage>(p => p.Add(x => x.Id, Cc).Add(x => x.ThreadId, locked.Id));
        thread.WaitForElement("[data-testid=thread-title]", Wait);
        AssertClean(thread.Markup, "thread", requireOneH1: false);
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Announcements_page_is_accessible(string culture)
    {
        await StartAsync(culture, Permissions.Forums.View, Permissions.Forums.Moderate, Permissions.LessonPlans.View);

        var page = Render<AnnouncementsPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=announcement-form]", Wait);

        AssertClean(page.Markup, "announcements", requireOneH1: false);
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Module_viewer_is_accessible_for_every_content_kind(string culture)
    {
        var asset = new AssetDto(Guid.NewGuid(), Cc, AssetKinds.Document, "worksheet.pdf", "application/pdf", 10, null, true, DateTime.UtcNow);
        var image = new AssetDto(Guid.NewGuid(), Cc, AssetKinds.Image, "map.png", "image/png", 10, null, true, DateTime.UtcNow);
        var video = new AssetDto(Guid.NewGuid(), Cc, AssetKinds.Video, "lesson.mp4", "video/mp4", 10, 60, true, DateTime.UtcNow);
        var modules = new[]
        {
            Seed("Story", ModuleKinds.Page, new ModuleContentInput(BodyHtmlEn: "<p>Hello <a href=\"https://a.test\">link</a></p>", BodyHtmlAr: "<p>مرحبا</p>"), rule: CompletionRules.Manual),
            Seed("Reference", ModuleKinds.Link, new ModuleContentInput(Url: "https://example.test/ref")),
            Seed("Worksheet", ModuleKinds.File, asset: asset, rule: CompletionRules.View),
            Seed("Map", ModuleKinds.File, asset: image),
            Seed("Clip", ModuleKinds.Video, asset: video),
        };
        await StartAsync(culture, Permissions.Modules.View, Permissions.Files.Download, Permissions.LessonPlans.View);

        foreach (var m in modules)
        {
            var page = Render<ModuleViewerPage>(p => p.Add(x => x.Id, Cc).Add(x => x.ModuleId, m.Node.Id));
            page.WaitForElement("[data-testid=module-title]", Wait);
            AssertClean(page.Markup, $"module viewer ({m.Node.Kind} {m.Node.TitleEn})");
        }
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Student_course_home_is_accessible(string culture)
    {
        Seed("Done first", ModuleKinds.Page, new ModuleContentInput(BodyHtmlEn: "<p>x</p>"), rule: CompletionRules.Manual);
        Seed("Read me next", ModuleKinds.Page, new ModuleContentInput(BodyHtmlEn: "<p>y</p>"), rule: CompletionRules.Manual);
        var nodes = _modules.NodesFor(Cc, Lesson);
        var plan = Plans.Plans[Cc];
        Plans.Plans[Cc] = plan with
        {
            Sections = plan.Sections.Select(s => s with
            {
                Lessons = s.Lessons.Select(l => l.Key == Lesson ? l with { Modules = nodes, Progress = new ProgressDto(0, 2) } : l with { Modules = [], Progress = null }).ToList(),
            }).ToList(),
            Progress = new ProgressDto(0, 2),
        };
        Plans.HideHiddenItems = true;
        await StartAsync(culture, Permissions.Modules.View, Permissions.LessonPlans.View);

        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=learn-header]", Wait);

        AssertClean(page.Markup, "student course home");
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Home_cards_are_accessible(string culture)
    {
        await StartAsync(culture, Permissions.Modules.View, Permissions.LessonPlans.View, Permissions.CourseAssignments.View, Permissions.Quizzes.View);

        var home = Render<Home>();
        home.WaitForElement("p, .mud-typography-body1", Wait);

        AssertClean(home.Markup, "home", requireOneH1: false);   // h1: #506
    }

    [Fact]
    public void The_audit_flags_a_nameless_icon_button_a_bare_input_and_a_second_h1()
    {
        var problems = Audit(Parse("<h1>a</h1><h1>b</h1><button><svg></svg></button><input type=\"text\">"));

        Assert.Contains(problems, p => p.StartsWith("control without a name"));
        Assert.Contains(problems, p => p.StartsWith("field without a label"));
        Assert.Contains(problems, p => p.Contains("exactly one h1"));
    }
}
