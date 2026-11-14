using System.Globalization;
using Bunit;
using Lms.Client.Pages.Attendance;
using Lms.Client.Pages.Excuses;
using Lms.Client.Pages.Me;
using Lms.Client.Services.Api;
using Lms.Client.Services.Attendance;
using Lms.Client.Services.Excuses;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Attendance;

/// <summary>
/// The M5 accessibility sweep for the attendance, excuse, alert and report pages (Task 39, X-10): the same axe-style scan over every page in both
/// languages. A new control without a name, a field without a label or a stacked table cell without its label fails here.
/// </summary>
public sealed class AttendanceA11yTests : StaffTestContext
{
    public AttendanceA11yTests()
    {
        Services.AddSingleton<IAttendanceApi>(new FakeAttendanceApi());
        Services.AddSingleton<IExcusesApi>(new FakeExcusesApi().Seed());
        Services.AddSingleton(new AttendanceDraftStore(new StorageJs(), TimeProvider.System));
        Services.AddSingleton<IClassesApi, FakeClassesApi>();
    }

    private async Task PrepareAsync(string culture, params string[] permissions)
    {
        FakeCultureService.Apply(new CultureInfo(culture));
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions);
        RenderProviders();
    }

    private static void AssertClean(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, string page, bool oneHeading = true)
    {
        var problems = A11yScan.Problems(cut, oneHeading);
        Assert.True(problems.Count == 0, $"{page}:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void The_scan_itself_catches_the_problems_it_claims_to()
    {
        var bad = Render((Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddMarkupContent(0,
            "<button><svg/></button><img src='x'><input type='text'><div aria-hidden='true'><a href='#'>x</a></div><table class='lms-stack-table'><tbody><tr><td>v</td></tr></tbody></table>")));

        var problems = A11yScan.Problems(bad);

        Assert.Contains(problems, p => p.StartsWith("control without an accessible name"));
        Assert.Contains(problems, p => p.StartsWith("image without alt"));
        Assert.Contains(problems, p => p.StartsWith("field without a label"));
        Assert.Contains(problems, p => p.StartsWith("focusable control inside aria-hidden"));
        Assert.Contains(problems, p => p.StartsWith("stacked table cell without data-label"));
        Assert.Contains(problems, p => p.StartsWith("expected one h1"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Today_take_and_live_proposal_pages_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Permissions.Attendance.Take, Permissions.Attendance.Edit);

        var today = Render<AttendanceTodayPage>();
        today.WaitForElement("[data-testid=attendance-sessions]");
        AssertClean(today, "today");
        today.Find($"[data-testid=session-history-{FakeAttendanceApi.SessionTaken}]").Click();
        today.WaitForElement("[data-testid=history-list]");
        AssertClean(today, "today with the history drawer");
        await DisposeComponentsAsync();

        RenderProviders();
        var take = Render<AttendanceTakePage>(p => p.Add(x => x.Id, FakeAttendanceApi.SessionOpen));
        take.WaitForElement("[data-testid=att-roster]");
        take.Find($"[data-testid=att-50000000-0000-0000-0000-000000000001-Absent]").Click(); // a note button appears
        AssertClean(take, "take");
        await DisposeComponentsAsync();

        RenderProviders();
        var proposal = Render<AttendanceTakePage>(p => p.Add(x => x.Id, FakeAttendanceApi.SessionProposal));
        proposal.WaitForElement("[data-testid=unmatched]");
        AssertClean(proposal, "live proposal with unmatched participants");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Own_attendance_excuses_and_the_submit_dialog_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Permissions.Attendance.ViewOwn, Permissions.Excuses.Submit, Permissions.Students.ViewChildren);

        var own = Render<MyAttendancePage>();
        own.WaitForElement("[data-testid=own-table]");
        AssertClean(own, "my attendance");
        own.Find($"[data-testid=own-explain-{FakeAttendanceApi.AbsentSession}]").Click();
        Dialogs.WaitForElement("[data-testid=excuse-submit]");
        AssertClean(Dialogs, "the excuse dialog", oneHeading: false);
        await DisposeComponentsAsync();

        RenderProviders();
        var mine = Render<MyExcusesPage>();
        mine.WaitForElement("[data-testid=excuses-empty], [data-testid=excuses-list]");
        AssertClean(mine, "my excuses");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Review_queue_drawer_alerts_and_reports_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Permissions.Excuses.Review, Permissions.Excuses.Escalate, Permissions.Attendance.View);

        var queue = Render<ExcuseReviewPage>();
        queue.WaitForElement("[data-testid^=review-open-]");
        AssertClean(queue, "review queue");
        queue.Find("[data-testid^=review-open-]").Click();
        queue.WaitForElement("[data-testid=review-approve]");
        AssertClean(queue, "review queue with the drawer");
        await DisposeComponentsAsync();

        RenderProviders();
        var alerts = Render<AttendanceAlertsPage>();
        alerts.WaitForElement("[data-testid^=alert-formula-]");
        AssertClean(alerts, "alerts");
        await DisposeComponentsAsync();

        RenderProviders();
        var reports = Render<AttendanceReportsPage>();
        reports.WaitForElement("[data-testid=reports-table]");
        AssertClean(reports, "reports");
    }

    [Fact]
    public async Task Every_status_button_is_a_radio_with_a_group_name_and_a_visible_word()
    {
        await PrepareAsync("en", Permissions.Attendance.Take);
        var take = Render<AttendanceTakePage>(p => p.Add(x => x.Id, FakeAttendanceApi.SessionOpen));
        take.WaitForElement("[data-testid=att-roster]");

        foreach (var group in take.FindAll("[role=radiogroup]"))
        {
            Assert.False(string.IsNullOrWhiteSpace(group.GetAttribute("aria-label"))); // the student's name
            var radios = group.QuerySelectorAll("[role=radio]");
            Assert.Equal(4, radios.Length);
            Assert.All(radios, r => Assert.False(string.IsNullOrWhiteSpace(r.QuerySelector(".lms-att-label")?.TextContent))); // the status in words, never an icon alone
            Assert.Single(radios, r => r.GetAttribute("tabindex") == "0"); // one tab stop per row
        }
    }

    [Fact]
    public async Task The_save_result_and_mark_all_message_are_announced_politely()
    {
        await PrepareAsync("en", Permissions.Attendance.Take);
        var take = Render<AttendanceTakePage>(p => p.Add(x => x.Id, FakeAttendanceApi.SessionOpen));
        take.WaitForElement("[data-testid=att-roster]");

        take.Find("[data-testid=att-mark-all]").Click();

        Assert.Equal("status", take.Find("[data-testid=att-mark-all-message]").GetAttribute("role"));
        Assert.Equal("polite", take.Find(".lms-att-counts").GetAttribute("aria-live")); // the counts change as marks are made
    }
}
