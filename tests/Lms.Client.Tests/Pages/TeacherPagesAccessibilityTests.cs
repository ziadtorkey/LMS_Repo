using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lms.Client.Pages.Assignments;
using Lms.Client.Pages.Quizzes;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages;

/// <summary>
/// M4a accessibility sweep (T28) for the teacher pages: assignment authoring and grading, quiz builder. The same structural
/// rules as the student-side sweep: every control has an accessible name, every field a label, images have alt text.
/// </summary>
public sealed class TeacherPagesAccessibilityTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;

    private readonly FakeCourseAssignmentsApi _assignments = new();
    private readonly FakeQuizzesApi _quizzes = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public TeacherPagesAccessibilityTests()
    {
        Services.AddSingleton<ICourseAssignmentsApi>(_assignments);
        Services.AddSingleton<IQuizzesApi>(_quizzes);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Services.AddSingleton<IModulesApi>(new FakeModulesApi());
        Services.AddSingleton<IClassCourseGroupsApi>(new FakeClassCourseGroupsApi());
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

    private static List<string> Audit(IDocument document)
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
        return problems;
    }

    private static void AssertClean(string markup, string page)
    {
        var problems = Audit(Parse(markup)).Distinct().ToList();
        Assert.True(problems.Count == 0, $"{page}: " + string.Join("\n", problems));
    }

    private async Task StartAsync(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.CourseAssignments.View, Permissions.CourseAssignments.Manage, Permissions.CourseAssignments.Grade,
            Permissions.Quizzes.View, Permissions.Quizzes.Manage, Permissions.Modules.Manage, Permissions.LessonPlans.View);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Assignment_pages_are_accessible(string culture)
    {
        await StartAsync(culture);

        var list = Render<CourseAssignmentsPage>(p => p.Add(x => x.Id, Cc));
        list.WaitForElement($"[data-testid=assignment-open-{FakeCourseAssignmentsApi.AssignmentId}]", Wait);
        AssertClean(list.Markup, "assignments list");

        var submissions = Render<SubmissionsPage>(p => p.Add(x => x.Id, FakeCourseAssignmentsApi.AssignmentId));
        submissions.WaitForElement("[data-testid=submissions-header]", Wait);
        submissions.WaitForAssertion(() => Assert.NotEmpty(submissions.FindAll("[data-testid^=status-]")), Wait);
        AssertClean(submissions.Markup, "submissions");

        var grade = Render<GradeSubmissionPage>(p => p.Add(x => x.Id, FakeCourseAssignmentsApi.LateSubmissionId));
        grade.WaitForElement("[data-testid=grade-form]", Wait);
        AssertClean(grade.Markup, "grading");
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Assignment_dialog_is_accessible(string culture)
    {
        await StartAsync(culture);
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<AssignmentDialog>("t", new DialogParameters<AssignmentDialog>
        {
            { x => x.ClassCourseId, Cc },
            { x => x.Existing, _assignments.Assignments[FakeCourseAssignmentsApi.AssignmentId] },
        });
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=assignment-save]")), Wait);

        AssertClean(_dialogs.Markup, "assignment dialog");
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task Quiz_builder_pages_are_accessible(string culture)
    {
        await StartAsync(culture);

        var list = Render<CourseQuizzesPage>(p => p.Add(x => x.Id, Cc));
        list.WaitForElement($"[data-testid=quiz-open-{FakeQuizzesApi.QuizId}]", Wait);
        AssertClean(list.Markup, "quizzes list");

        var builder = Render<QuizBuilderPage>(p => p.Add(x => x.Id, FakeQuizzesApi.QuizId));
        builder.WaitForElement("[data-testid=quiz-heading]", Wait);
        AssertClean(builder.Markup, "quiz builder (questions and settings)");

        await builder.InvokeAsync(() => builder.FindComponent<MudTabs>().Instance.ActivatePanelAsync(2));
        builder.WaitForElement("[data-testid^=preview-q-]", Wait);
        AssertClean(builder.Markup, "quiz builder (preview)");
    }

    [Fact]
    public void The_audit_flags_a_nameless_icon_button()
    {
        var bad = Render<MudIconButton>(p => p.Add(x => x.Icon, Icons.Material.Filled.Delete));
        Assert.NotEmpty(Audit(Parse(bad.Markup)));
    }
}
