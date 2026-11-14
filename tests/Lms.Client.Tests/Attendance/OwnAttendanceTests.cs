using System.Globalization;
using Bunit;
using Lms.Client.Pages.Excuses;
using Lms.Client.Pages.Guardians;
using Lms.Client.Pages.Me;
using Lms.Client.Services.Attendance;
using Lms.Client.Services.Excuses;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Attendance;

public sealed class OwnAttendanceTests : StaffTestContext
{
    private static readonly Guid Child = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private readonly FakeAttendanceApi _attendance = new();
    private readonly FakeExcusesApi _excuses = new();

    public OwnAttendanceTests()
    {
        Services.AddSingleton<IAttendanceApi>(_attendance);
        Services.AddSingleton<IExcusesApi>(_excuses);
    }

    private async Task<IRenderedComponent<MyAttendancePage>> RenderOwnAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Attendance.ViewOwn, Permissions.Excuses.Submit);
        RenderProviders();
        var page = Render<MyAttendancePage>();
        page.WaitForElement("[data-testid=own-table]");
        return page;
    }

    [Fact]
    public async Task Counts_and_rate_are_the_servers()
    {
        var page = await RenderOwnAsync();

        Assert.Equal("80%", page.Find("[data-testid=own-percent]").TextContent.Trim());
        Assert.Equal("2", page.Find("[data-testid=own-count-Absent]").TextContent.Trim());
        Assert.Equal("1", page.Find("[data-testid=own-count-Late]").TextContent.Trim());
    }

    [Fact]
    public async Task Action_follows_can_submit_excuse()
    {
        var page = await RenderOwnAsync();

        Assert.NotNull(page.Find("[data-testid=own-submit-excuse]"));
        Assert.NotNull(page.Find($"[data-testid=own-explain-{FakeAttendanceApi.AbsentSession}]")); // an excusable absence
        Assert.NotNull(page.Find($"[data-testid=own-explain-{FakeAttendanceApi.LateSession}]")); // an excusable late
        Assert.Empty(page.FindAll($"[data-testid=own-explain-{FakeAttendanceApi.PendingSession}]")); // already covered by an excuse
        Assert.Contains("Submitted", page.Find($"[data-testid=own-excuse-{FakeAttendanceApi.PendingSession}]").TextContent);
        Assert.Contains("Approved", page.Find($"[data-testid=own-excuse-{FakeAttendanceApi.ExcusedSession}]").TextContent);
    }

    [Fact]
    public async Task Student_under_guardians_only_sees_the_guardian_sentence()
    {
        _attendance.CanSubmitExcuse = false;
        var page = await RenderOwnAsync();

        Assert.Contains("guardian can submit", page.Find("[data-testid=own-guardian-sentence]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=own-submit-excuse]"));
        Assert.Empty(page.FindAll("[data-testid^=own-explain-]"));
    }

    [Fact]
    public async Task A_guardian_reads_the_child_without_the_student_sentence()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Attendance.ViewOwn, Permissions.Excuses.Submit);
        RenderProviders();

        var page = Render<ChildAttendancePage>(p => p.Add(x => x.StudentId, Child));
        page.WaitForElement("[data-testid=own-table]");

        Assert.Contains("Ahmed Saeed", page.Markup);
        Assert.Empty(page.FindAll("[data-testid=own-guardian-sentence]"));
        Assert.Contains($"me/excuses?studentId={Child}", page.Find("[data-testid=own-excuses-link]").GetAttribute("href"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderOwnAsync("ar");

        Assert.Equal("الحضور", page.Find("[data-testid=own-title]").TextContent.Trim());
        Assert.Contains("نسبة الحضور", page.Markup);
        Assert.Equal("ltr", page.Find($"[data-testid=own-row-{FakeAttendanceApi.AbsentSession}] .lms-date-cell").GetAttribute("dir"));
    }
}

public sealed class MyExcusesPageTests : StaffTestContext
{
    private readonly FakeExcusesApi _excuses = new();

    public MyExcusesPageTests() => Services.AddSingleton<IExcusesApi>(_excuses);

    private async Task<IRenderedComponent<MyExcusesPage>> RenderAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Excuses.Submit);
        RenderProviders();
        return Render<MyExcusesPage>();
    }

    private Task<ExcuseDto> SubmitAsync() =>
        _excuses.SubmitAsync(new SubmitExcuseRequest(FakeExcusesApi.Student, "Illness", "Fever", new DateOnly(2026, 11, 3), null, null, null));

    [Fact]
    public async Task Empty_state_says_what_to_do_next()
    {
        var page = await RenderAsync();

        Assert.Contains("Explain this absence", page.WaitForElement("[data-testid=excuses-empty]").TextContent);
    }

    [Fact]
    public async Task Withdraw_only_while_submitted()
    {
        var pending = await SubmitAsync();
        var decided = await SubmitAsync();
        _excuses.CurrentUser = FakeExcusesApi.Counselor;
        await _excuses.ApproveAsync(decided.Id, new ApproveExcuseRequest("OK"));
        _excuses.CurrentUser = FakeExcusesApi.Guardian;
        var page = await RenderAsync();

        page.WaitForElement("[data-testid=excuses-list]");
        Assert.NotNull(page.Find($"[data-testid=excuse-withdraw-{pending.Id}]"));
        Assert.Empty(page.FindAll($"[data-testid=excuse-withdraw-{decided.Id}]"));
        Assert.Contains("Approved", page.Find($"[data-testid=excuse-status-{decided.Id}]").TextContent);
        Assert.Contains("OK", page.Find($"[data-testid=excuse-comment-{decided.Id}]").TextContent);

        page.Find($"[data-testid=excuse-withdraw-{pending.Id}]").Click();
        page.WaitForAssertion(() => Assert.Contains("Withdrawn", page.Find($"[data-testid=excuse-status-{pending.Id}]").TextContent));
        Assert.Empty(page.FindAll($"[data-testid=excuse-withdraw-{pending.Id}]"));
    }

    [Fact]
    public async Task History_lists_every_step()
    {
        var excuse = await SubmitAsync();
        var page = await RenderAsync();

        page.WaitForElement("[data-testid=excuses-list]");

        Assert.Single(page.FindAll($"[data-testid=excuse-history-{excuse.Id}] li"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        await SubmitAsync();
        var page = await RenderAsync("ar");

        page.WaitForElement("[data-testid=excuses-list]");

        Assert.Equal("أعذاري", page.Find("[data-testid=excuses-title]").TextContent.Trim());
        Assert.Contains("مقدَّم", page.Find("[data-testid^=excuse-status-]").TextContent); // the status in Arabic, as a word
    }
}
