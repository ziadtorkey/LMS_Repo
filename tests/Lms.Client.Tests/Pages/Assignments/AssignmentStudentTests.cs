using System.Globalization;
using Bunit;
using Lms.Client.Components.Assets;
using Lms.Client.Components.Editors;
using Lms.Client.Pages.Assignments;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages.Assignments;

/// <summary>Assignments, student side (M4a T21): view, submit, resubmit, feedback.</summary>
public sealed class AssignmentStudentTests : LessonPlansTestContext
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly DateTime Due = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeCourseAssignmentsApi _api = new();
    private readonly ManualTime _time = new();
    private Guid _id = FakeCourseAssignmentsApi.AssignmentId;

    public AssignmentStudentTests()
    {
        Services.AddSingleton<ICourseAssignmentsApi>(_api);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Services.AddSingleton<TimeProvider>(_time);
        Culture.Current = new CultureInfo("en");
        _api.ServerNow = () => _time.Now.UtcDateTime; // server and browser agree unless a test says otherwise
        Configure(a => a); // due 2026-12-01 09:00
    }

    private void Configure(Func<CourseAssignmentDto, CourseAssignmentDto> change) =>
        _api.Assignments[_id] = change(_api.Assignments[_id] with { DueAtUtc = Due });

    private async Task<IRenderedComponent<AssignmentPage>> OpenAsync(params string[] extra)
    {
        await GrantAsync([Permissions.CourseAssignments.View, Permissions.CourseAssignments.Submit, .. extra]);
        RenderProviders();
        var page = Render<AssignmentPage>(p => p.Add(x => x.Id, Cc).Add(x => x.AssignmentId, _id));
        page.WaitForElement("[data-testid=assignment-title], [data-testid=assignment-error], [data-testid=assignment-forbidden]", Wait);
        return page;
    }

    private static Task SetTextAsync(IRenderedComponent<AssignmentPage> page, string html) =>
        page.InvokeAsync(() => page.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync(html));

    [Fact]
    public async Task Blocked_late_policy_disables_submit()
    {
        Configure(a => a with { LatePolicy = LatePolicies.Block, PenaltyPercent = null });
        _time.Now = new DateTimeOffset(Due.AddHours(2));

        var page = await OpenAsync();

        Assert.NotNull(page.Find("[data-testid=submit-blocked]"));
        Assert.Contains("no longer accepts", page.Find("[data-testid=submit-blocked]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=submit-send]"));
    }

    [Fact]
    public async Task Resubmit_only_when_rule_allows()
    {
        var submitted = new SubmissionDto(Guid.NewGuid(), _id, Guid.NewGuid(), "أنا", "Me", SubmissionStatuses.Submitted, "<p>v1</p>", [], Due.AddHours(-3), false, 1, null);
        _api.Mine[_id] = submitted;

        Configure(a => a with { ResubmissionRule = ResubmissionRules.None });
        var none = await OpenAsync();
        Assert.NotNull(none.Find("[data-testid=submit-blocked]"));
        Assert.Contains("Resubmitting is not allowed", none.Find("[data-testid=submit-blocked]").TextContent);
    }

    [Fact]
    public async Task Resubmit_is_offered_until_due_when_the_rule_says_so()
    {
        _api.Mine[_id] = new SubmissionDto(Guid.NewGuid(), _id, Guid.NewGuid(), "أنا", "Me", SubmissionStatuses.Submitted, "<p>v1</p>", [], Due.AddHours(-3), false, 1, null);
        Configure(a => a with { ResubmissionRule = ResubmissionRules.UntilDue });

        var page = await OpenAsync();

        Assert.Contains("Resubmit", page.Find("[data-testid=submit-send]").TextContent);
        await SetTextAsync(page, "<p>v2</p>");
        page.Find("[data-testid=submit-send]").Click();

        page.WaitForAssertion(() => Assert.Equal("<p>v2</p>", Assert.Single(_api.SubmitRequests).TextHtml));
        Assert.Equal(2, _api.Mine[_id].Number);
    }

    [Fact]
    public async Task Resubmit_after_due_is_refused_under_UntilDue()
    {
        _api.Mine[_id] = new SubmissionDto(Guid.NewGuid(), _id, Guid.NewGuid(), "أنا", "Me", SubmissionStatuses.Submitted, "<p>v1</p>", [], Due.AddHours(-3), false, 1, null);
        Configure(a => a with { ResubmissionRule = ResubmissionRules.UntilDue, LatePolicy = LatePolicies.Allow, PenaltyPercent = null });
        _time.Now = new DateTimeOffset(Due.AddHours(1));

        var page = await OpenAsync();

        Assert.NotNull(page.Find("[data-testid=submit-blocked]"));
    }

    [Fact]
    public async Task Submit_requires_text_or_file()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=submit-send]").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=submit-error]")));
        Assert.Empty(_api.SubmitRequests);

        await SetTextAsync(page, "<p>Done</p>");
        page.Find("[data-testid=submit-send]").Click();
        page.WaitForAssertion(() => Assert.Single(_api.SubmitRequests));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=assignment-handed-in]")));
    }

    [Fact]
    public async Task Overdue_uses_server_time()
    {
        // The browser still thinks it is early; the server says the deadline has passed.
        _time.Now = new DateTimeOffset(Due.AddHours(-1));
        _api.ServerNow = () => Due.AddHours(2);

        var page = await OpenAsync();

        Assert.Contains("overdue by 2 h", page.Find("[data-testid=assignment-countdown]").TextContent);
        Assert.Contains("lose 10%", page.Find("[data-testid=assignment-late-note]").TextContent);
    }

    [Fact]
    public async Task Countdown_before_the_deadline_counts_down_by_the_server_offset()
    {
        _time.Now = new DateTimeOffset(Due.AddHours(-10)); // browser clock is 9 h behind...
        _api.ServerNow = () => Due.AddHours(-1);           // ...the server says one hour is left

        var page = await OpenAsync();

        Assert.Contains("due in 1 h", page.Find("[data-testid=assignment-countdown]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=assignment-late-note]"));
    }

    [Fact]
    public async Task Graded_shows_feedback()
    {
        _api.Mine[_id] = new SubmissionDto(Guid.NewGuid(), _id, Guid.NewGuid(), "أنا", "Me", SubmissionStatuses.Returned, "<p>mine</p>", [],
            Due.AddHours(2), true, 1, new SubmissionGradeDto(18m, 10, 16.2m, "<p>Well argued</p>", null, Due.AddDays(2), Due.AddDays(3)));
        Configure(a => a with { ResubmissionRule = ResubmissionRules.None });
        _time.Now = new DateTimeOffset(Due.AddDays(4));

        var page = await OpenAsync();

        Assert.Contains("16.2", page.Find("[data-testid=assignment-final-grade]").TextContent);
        Assert.Contains("10%", page.Find("[data-testid=assignment-penalty]").TextContent);
        Assert.Contains("Well argued", page.Find("[data-testid=assignment-feedback]").TextContent);
    }

    [Fact]
    public async Task A_graded_but_not_returned_grade_stays_hidden()
    {
        _api.Mine[_id] = new SubmissionDto(Guid.NewGuid(), _id, Guid.NewGuid(), "أنا", "Me", SubmissionStatuses.Graded, "<p>mine</p>", [],
            Due.AddHours(-2), false, 1, new SubmissionGradeDto(20m, null, 20m, "<p>secret</p>", null, Due, null));

        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid=assignment-grade]"));
        Assert.DoesNotContain("secret", page.Markup);
        Assert.NotNull(page.Find("[data-testid=assignment-waiting-return]"));
    }

    [Fact]
    public async Task Without_Submit_permission_the_page_is_read_only()
    {
        await GrantAsync(Permissions.CourseAssignments.View);
        RenderProviders();
        var page = Render<AssignmentPage>(p => p.Add(x => x.Id, Cc).Add(x => x.AssignmentId, _id));
        page.WaitForElement("[data-testid=submit-no-permission]", Wait);
    }

    [Fact]
    public async Task An_uploaded_file_can_be_removed_before_submitting()
    {
        var page = await OpenAsync();
        var keep = new AssetDto(Guid.NewGuid(), Cc, AssetKinds.Document, "keep.pdf", "application/pdf", 1, null, true, DateTime.UtcNow);
        var drop = new AssetDto(Guid.NewGuid(), Cc, AssetKinds.Document, "drop.pdf", "application/pdf", 1, null, true, DateTime.UtcNow);
        await page.InvokeAsync(() => page.FindComponent<AssetUploader>().Instance.OnUploaded.InvokeAsync(keep));
        await page.InvokeAsync(() => page.FindComponent<AssetUploader>().Instance.OnUploaded.InvokeAsync(drop));

        page.Find($"[data-testid=submit-file-remove-{drop.Id}]").Click();
        page.Find("[data-testid=submit-send]").Click();

        page.WaitForAssertion(() => Assert.Single(_api.SubmitRequests));
        Assert.Equal([keep.Id], _api.SubmitRequests.Single().FileAssetIds);
    }

    [Fact]
    public async Task Countdown_units_are_localized()
    {
        Culture.Current = new CultureInfo("ar");
        _time.Now = new DateTimeOffset(Due.AddHours(-30));
        _api.ServerNow = () => Due.AddHours(-30);

        var page = await OpenAsync();

        var text = page.Find("[data-testid=assignment-countdown]").TextContent;
        Assert.Contains("1 يوم 6 ساعة", text);
        Assert.DoesNotContain(" d ", text);
    }

    [Fact]
    public async Task Assignment_page_is_arabic_with_left_to_right_dates()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await OpenAsync();

        Assert.Equal("ltr", page.Find("[data-testid=assignment-due] span").GetAttribute("dir"));
        Assert.Contains("تسليم", page.Find("[data-testid=submit-send]").TextContent);
        Assert.Contains("عملك", page.Find("[data-testid=submit-panel]").TextContent);
    }

    [Fact]
    public async Task Due_date_is_an_unambiguous_left_to_right_value_on_its_own_line_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await OpenAsync();

        var date = page.Find("[data-testid=assignment-due-date]");
        Assert.Equal("ltr", date.GetAttribute("dir"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", date.TextContent); // no am/pm marks to scramble the order
        Assert.DoesNotContain(date.TextContent, page.Find("[data-testid=assignment-countdown]").TextContent);
    }
}
