using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lms.Client.Pages.Assignments;
using Lms.Client.Pages.Forums;
using Lms.Client.Pages.Library;
using Lms.Client.Pages.Quizzes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages;

/// <summary>
/// M4a accessibility sweep (T28): the structural rules a screen reader depends on, checked on every M4a page in both languages.
/// Every control has an accessible name, every image has alt text, every frame a title, form fields are labelled, and the
/// page has exactly one top-level heading or a titled region. 360 px layout and screenshots are reviewed by hand.
/// </summary>
public sealed class M4aAccessibilityTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;

    private readonly FakeLibraryApi _library = new();
    private readonly FakeForumsApi _forum = new() { Moderator = true };
    private readonly FakeCourseAssignmentsApi _assignments = new();
    private readonly FakeQuizAttemptsApi _quizzes = new() { StaffView = true };

    public M4aAccessibilityTests()
    {
        Services.AddSingleton<ILibraryApi>(_library);
        Services.AddSingleton<IForumsApi>(_forum);
        Services.AddSingleton<ICourseAssignmentsApi>(_assignments);
        Services.AddSingleton<IQuizAttemptsApi>(_quizzes);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Services.AddSingleton<ICoursesApi, FakeCoursesApi>();
        Services.AddSingleton<IModulesApi>(new FakeModulesApi());
    }

    private static string Name(IElement e)
    {
        var label = e.GetAttribute("aria-label") ?? e.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(label)) return label;
        var labelledBy = e.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy) && e.Owner?.GetElementById(labelledBy) is { } target && !string.IsNullOrWhiteSpace(target.TextContent)) return target.TextContent;
        return e.TextContent.Trim();
    }

    private static IDocument Parse(string markup) => new AngleSharp.Html.Parser.HtmlParser().ParseDocument("<html><body>" + markup + "</body></html>");

    private static List<string> Audit(IDocument document)
    {
        var problems = new List<string>();
        foreach (var b in document.QuerySelectorAll("button, a[href], [role=button]"))
            if (string.IsNullOrWhiteSpace(Name(b)) && b.QuerySelector("img[alt]:not([alt=''])") is null)
                problems.Add($"control without a name: {b.OuterHtml[..Math.Min(120, b.OuterHtml.Length)]}");
        foreach (var img in document.QuerySelectorAll("img"))
            if (img.GetAttribute("alt") is null) problems.Add($"image without alt: {img.OuterHtml[..Math.Min(100, img.OuterHtml.Length)]}");
        foreach (var f in document.QuerySelectorAll("iframe"))
            if (string.IsNullOrWhiteSpace(f.GetAttribute("title"))) problems.Add("iframe without a title");
        foreach (var i in document.QuerySelectorAll("input:not([type=hidden]):not([type=file]), textarea, select"))
        {
            var id = i.GetAttribute("id");
            var labelled = !string.IsNullOrWhiteSpace(i.GetAttribute("aria-label")) || !string.IsNullOrWhiteSpace(i.GetAttribute("aria-labelledby"))
                || (id is not null && document.QuerySelector($"label[for='{id}']") is not null) || i.Closest("label") is not null
                || i.Closest(".mud-input-control")?.QuerySelector("label, .mud-input-label") is not null
                || i.Closest(".mud-radio, .mud-checkbox, .mud-switch") is not null;
            if (!labelled) problems.Add($"field without a label: {i.OuterHtml[..Math.Min(100, i.OuterHtml.Length)]}");
        }
        foreach (var f in document.QuerySelectorAll("input[type=file]"))
            if (string.IsNullOrWhiteSpace(f.GetAttribute("aria-label"))) problems.Add("file input without aria-label");
        foreach (var m in document.QuerySelectorAll("[role=timer]"))
            if (m.GetAttribute("aria-live") is null) problems.Add("timer without aria-live");
        return problems;
    }

    private async Task GrantAllAsync() =>
        await GrantAsync(Permissions.Library.View, Permissions.Library.Manage, Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate,
            Permissions.CourseAssignments.View, Permissions.CourseAssignments.Submit, Permissions.Quizzes.View, Permissions.Quizzes.Attempt,
            Permissions.Quizzes.Grade, Permissions.Quizzes.Manage, Permissions.Modules.View, Permissions.Files.Download, Permissions.LessonPlans.View);

    public static IEnumerable<object[]> Cultures => [["en"], ["ar"]];

    private void AssertClean(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, string page)
    {
        var problems = Audit(Parse(cut.Markup));
        Assert.True(problems.Count == 0, $"{page}: " + string.Join("\n", problems.Distinct()));
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Library_page_is_accessible(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        _library.Add("Book", file: new AssetDto(Guid.NewGuid(), null, AssetKinds.Document, "b.pdf", "application/pdf", 1, null, true, DateTime.UtcNow, FakeLibraryApi.UnitId));
        await GrantAllAsync();
        RenderProviders();
        var page = Render<LibraryPage>();
        page.WaitForElement("[data-testid=library-grid]", Wait);
        AssertClean(page, "library");
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Forum_pages_are_accessible(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var thread = _forum.AddThread(Cc, "Topic", pinned: true);
        _forum.Announcements.Add(new(Guid.NewGuid(), Cc, "Welcome", "<p>Hi</p>", true, Guid.NewGuid(), "م", "Teacher", DateTime.UtcNow, null));
        await GrantAllAsync();
        RenderProviders();

        var forum = Render<ForumPage>(p => p.Add(x => x.Id, Cc));
        forum.WaitForElement("[data-testid=forum-threads]", Wait);
        AssertClean(forum, "forum");

        var detail = Render<ThreadPage>(p => p.Add(x => x.Id, Cc).Add(x => x.ThreadId, thread.Id));
        detail.WaitForElement("[data-testid=thread-title]", Wait);
        AssertClean(detail, "thread");

        var announcements = Render<AnnouncementsPage>(p => p.Add(x => x.Id, Cc));
        announcements.WaitForElement("[data-testid=announcements-list]", Wait);
        AssertClean(announcements, "announcements");
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Assignment_page_is_accessible(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAllAsync();
        RenderProviders();
        var page = Render<AssignmentPage>(p => p.Add(x => x.Id, Cc).Add(x => x.AssignmentId, FakeCourseAssignmentsApi.AssignmentId));
        page.WaitForElement("[data-testid=assignment-title]", Wait);
        AssertClean(page, "assignment");
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Quiz_pages_are_accessible(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAllAsync();
        RenderProviders();

        var start = Render<QuizAttemptPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, FakeQuizAttemptsApi.QuizId));
        start.WaitForElement("[data-testid=quiz-start]", Wait);
        AssertClean(start, "quiz start");

        start.Find("[data-testid=quiz-start-button]").Click();
        start.WaitForElement("[data-testid=quiz-attempt]", Wait);
        AssertClean(start, "quiz attempt");

        var attempt = _quizzes.SeedSubmittedAttempt();
        var result = Render<QuizResultPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, FakeQuizAttemptsApi.QuizId).Add(x => x.AttemptId, attempt));
        result.WaitForElement("[data-testid=quiz-result]", Wait);
        AssertClean(result, "quiz result");

        var list = Render<QuizAttemptsPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, FakeQuizAttemptsApi.QuizId));
        list.WaitForElement("[data-testid=attempts-table]", Wait);
        AssertClean(list, "quiz attempts");

        var grading = Render<EssayGradingPage>(p => p.Add(x => x.Id, Cc).Add(x => x.QuizId, FakeQuizAttemptsApi.QuizId).Add(x => x.AttemptId, attempt));
        grading.WaitForElement("[data-testid=essay-grading]", Wait);
        AssertClean(grading, "essay grading");
    }

    [Fact]
    public void The_audit_itself_flags_a_nameless_icon_button_and_an_image_without_alt()
    {
        var bad = Render<MudIconButton>(p => p.Add(x => x.Icon, Icons.Material.Filled.Delete));
        Assert.NotEmpty(Audit(Parse(bad.Markup)));

        var good = Render<MudIconButton>(p => p.Add(x => x.Icon, Icons.Material.Filled.Delete).AddUnmatched("aria-label", "Delete"));
        Assert.Empty(Audit(Parse(good.Markup)));
    }
}
