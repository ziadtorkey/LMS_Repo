using System.Globalization;
using Bunit;
using Lms.Client.Components.Assets;
using Lms.Client.Components.Content;
using Lms.Client.Layout;
using Lms.Client.Pages.Assignments;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Assignments;

/// <summary>Assignments, teacher side (M4a T20): authoring dialog, submissions list and grading.</summary>
public sealed class AssignmentTeacherTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;

    private readonly FakeCourseAssignmentsApi _assignments = new();
    private readonly FakeAssetsApi _assets = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public AssignmentTeacherTests()
    {
        Services.AddSingleton<ICourseAssignmentsApi>(_assignments);
        Services.AddSingleton<IAssetsApi>(_assets);
    }

    private async Task StartAsync(string culture = "en", params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions.Length > 0 ? permissions
            : [Permissions.CourseAssignments.View, Permissions.CourseAssignments.Manage, Permissions.CourseAssignments.Grade]);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private void OpenDialog(CourseAssignmentDto? existing)
    {
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<AssignmentDialog>("t", new DialogParameters<AssignmentDialog>
        {
            { x => x.ClassCourseId, Cc },
            { x => x.Existing, existing },
            { x => x.Lessons, new List<ModuleLessonOption> { new(Guid.NewGuid(), "Lesson 1") } },
        });
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=assignment-save]")), Wait);
    }

    private IRenderedComponent<SubmissionsPage> OpenSubmissions()
    {
        RenderProviders();
        var page = Render<SubmissionsPage>(p => p.Add(x => x.Id, FakeCourseAssignmentsApi.AssignmentId));
        page.WaitForElement("[data-testid=submissions-header]", Wait);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid^=status-]")), Wait);
        return page;
    }

    private IRenderedComponent<GradeSubmissionPage> OpenGrade(Guid submissionId)
    {
        RenderProviders();
        var page = Render<GradeSubmissionPage>(p => p.Add(x => x.Id, submissionId));
        page.WaitForElement("[data-testid=grade-form]", Wait);
        return page;
    }

    private CourseAssignmentDto Existing() => _assignments.Assignments[FakeCourseAssignmentsApi.AssignmentId];

    [Fact]
    public async Task Late_policy_penalty_requires_percentage()
    {
        await StartAsync();
        var noPercent = Existing() with { LatePolicy = LatePolicies.Penalty, PenaltyPercent = null };
        OpenDialog(noPercent);

        _dialogs.Find("[data-testid=assignment-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.Contains("penalty from 1 to 100", _dialogs.Markup), Wait);
        Assert.DoesNotContain("update", _assignments.Calls);
    }

    [Fact]
    public async Task Penalty_with_a_percentage_saves_the_assignment()
    {
        await StartAsync();
        OpenDialog(Existing());   // Penalty 10 %

        _dialogs.Find("[data-testid=assignment-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.Contains("update", _assignments.Calls), Wait);
        Assert.Equal(10, _assignments.Assignments[FakeCourseAssignmentsApi.AssignmentId].PenaltyPercent);
    }

    [Fact]
    public async Task Server_late_policy_error_is_shown_on_the_penalty_field()
    {
        await StartAsync();
        OpenDialog(Existing());
        _assignments.Failure = new ApiProblemException(400, Lms.Shared.ProblemCodes.late_policy_invalid, null, null);

        _dialogs.Find("[data-testid=assignment-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.Contains("needs a penalty of 1", _dialogs.Markup), Wait);
    }

    [Fact]
    public async Task Dialog_requires_a_submission_type_and_titles()
    {
        await StartAsync();
        var none = Existing() with { AllowText = false, AllowFiles = false };
        OpenDialog(none);

        _dialogs.Find("[data-testid=assignment-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.Contains("Allow text, files or both", _dialogs.Markup), Wait);
        Assert.DoesNotContain("update", _assignments.Calls);
    }

    [Fact]
    public async Task Submissions_show_late_badge()
    {
        await StartAsync();
        var page = OpenSubmissions();

        var omar = _assignments.Submissions[FakeCourseAssignmentsApi.LateSubmissionId].StudentId;
        var sara = _assignments.Submissions[FakeCourseAssignmentsApi.OnTimeSubmissionId].StudentId;
        Assert.Contains("Late", page.Find($"[data-testid=late-{omar}]").TextContent);
        Assert.Empty(page.FindAll($"[data-testid=late-{sara}]"));
        Assert.Contains("Submitted", page.Find($"[data-testid=status-{sara}]").TextContent);
        // a student with no hand-in is listed too, without a grade link
        Assert.Contains("Not handed in", page.Markup);
        Assert.Equal(3, page.FindAll("[data-testid^=grade-]").Count(e => e.GetAttribute("href")?.EndsWith("/grade") == true));
    }

    [Fact]
    public async Task Submissions_status_filter_narrows_the_list()
    {
        await StartAsync();
        var page = OpenSubmissions();

        await page.InvokeAsync(() => page.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync("Late"));

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid^=status-]")), Wait);
        Assert.Contains("Omar", page.Markup);
    }

    [Fact]
    public async Task Grade_above_max_rejected()
    {
        await StartAsync();
        var page = OpenGrade(FakeCourseAssignmentsApi.OnTimeSubmissionId);

        page.Find("[data-testid=grade-input]").Input("25");
        page.Find("[data-testid=grade-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("cannot be above 20", page.Markup), Wait);
        Assert.Empty(_assignments.GradeRequests);
    }

    [Fact]
    public async Task Annotated_file_attached_to_feedback()
    {
        await StartAsync();
        var page = OpenGrade(FakeCourseAssignmentsApi.OnTimeSubmissionId);
        var annotated = new AssetDto(Guid.NewGuid(), Cc, AssetKinds.Document, "marked.pdf", "application/pdf", 1200, null, true, DateTime.UtcNow);

        await page.InvokeAsync(() => page.FindComponent<AssetUploader>().Instance.OnUploaded.InvokeAsync(annotated));
        page.Find("[data-testid=grade-input]").Input("17");
        page.Find("[data-testid=grade-return]").Click();

        page.WaitForAssertion(() => Assert.Single(_assignments.GradeRequests), Wait);
        var (id, request) = _assignments.GradeRequests.Single();
        Assert.Equal(FakeCourseAssignmentsApi.OnTimeSubmissionId, id);
        Assert.Equal(17m, request.Grade);
        Assert.Equal(annotated.Id, request.AnnotatedFileAssetId);
        Assert.Contains("return", _assignments.Calls);
        Assert.Equal(SubmissionStatuses.Returned, _assignments.Submissions[id].Status);
    }

    [Fact]
    public async Task Late_submission_shows_the_penalty_preview()
    {
        await StartAsync();
        var page = OpenGrade(FakeCourseAssignmentsApi.LateSubmissionId);

        page.Find("[data-testid=grade-input]").Input("20");

        page.WaitForAssertion(() => Assert.Contains("final grade 18", page.Find("[data-testid=grade-penalty]").TextContent), Wait);
        Assert.NotEmpty(page.FindAll("[data-testid=grade-late]"));
    }

    [Fact]
    public async Task Save_without_return_keeps_the_work_unreleased()
    {
        await StartAsync();
        var page = OpenGrade(FakeCourseAssignmentsApi.OnTimeSubmissionId);

        page.Find("[data-testid=grade-input]").Input("15");
        page.Find("[data-testid=grade-save]").Click();

        page.WaitForAssertion(() => Assert.Single(_assignments.GradeRequests), Wait);
        Assert.DoesNotContain("return", _assignments.Calls);
        Assert.Equal(SubmissionStatuses.Graded, _assignments.Submissions[FakeCourseAssignmentsApi.OnTimeSubmissionId].Status);
    }

    [Fact]
    public async Task Grading_page_is_forbidden_without_the_grade_permission()
    {
        await StartAsync("en", Permissions.CourseAssignments.View);
        RenderProviders();

        var page = Render<GradeSubmissionPage>(p => p.Add(x => x.Id, FakeCourseAssignmentsApi.OnTimeSubmissionId));

        page.WaitForElement("[data-testid=grade-forbidden]", Wait);
        Assert.Empty(_assignments.Calls);
    }

    [Fact]
    public async Task Class_course_list_shows_assignments_and_the_new_button_for_managers()
    {
        await StartAsync();
        RenderProviders();

        var page = Render<CourseAssignmentsPage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement($"[data-testid=assignment-open-{FakeCourseAssignmentsApi.AssignmentId}]", Wait);
        Assert.Contains("Reading homework", page.Markup);
        Assert.Contains("3 submitted", page.Markup);
        Assert.NotEmpty(page.FindAll("[data-testid=assignments-new]"));
    }

    [Fact]
    public async Task Class_course_list_hides_the_new_button_without_manage()
    {
        await StartAsync("en", Permissions.CourseAssignments.View);
        RenderProviders();

        var page = Render<CourseAssignmentsPage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement("[data-testid=assignments-heading]", Wait);
        Assert.Empty(page.FindAll("[data-testid=assignments-new]"));
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        await StartAsync("ar");
        PlaceContextAtUnit();
        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b =>
        {
            b.OpenComponent<SubmissionsPage>(0);
            b.AddComponentParameter(1, "Id", FakeCourseAssignmentsApi.AssignmentId);
            b.CloseComponent();
        })));
        layout.WaitForElement("[data-testid=submissions-header]", Wait);
        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("[data-testid^=status-]")), Wait);

        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("واجب القراءة", layout.Find("[data-testid=submissions-header]").TextContent);
        Assert.Contains("متأخر", layout.Markup);
        Assert.Contains("لم يسلّم", layout.Markup);
    }
}
