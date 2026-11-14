using System.Globalization;
using Bunit;
using Lms.Client.Pages.Attendance;
using Lms.Client.Services.Attendance;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Attendance;

public sealed class AttendanceAlertsPageTests : StaffTestContext
{
    private readonly FakeAttendanceApi _api = new();

    public AttendanceAlertsPageTests() => Services.AddSingleton<IAttendanceApi>(_api);

    private async Task<IRenderedComponent<AttendanceAlertsPage>> RenderAsync(string culture = "en", params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions.Length == 0 ? [Permissions.Excuses.Review] : permissions);
        RenderProviders();
        var page = Render<AttendanceAlertsPage>();
        page.WaitForElement("[data-testid^=alert-formula-]");
        return page;
    }

    [Fact]
    public async Task Alert_rows_show_the_formula_inputs()
    {
        var page = await RenderAsync();

        var formulas = page.FindAll("[data-testid^=alert-formula-]").Select(f => f.TextContent.Trim()).ToList();

        Assert.Contains("5 absences + 1 lates = 5.5 (threshold 5, last 30 days)", formulas); // the server's counts and score, not summed here
        Assert.Contains("4 absences + 3 lates = 5.5 (threshold 5, last 30 days)", formulas);
        Assert.Equal(2, formulas.Count); // active only by default
    }

    [Fact]
    public async Task Cleared_alerts_appear_when_active_only_is_switched_off()
    {
        var page = await RenderAsync();

        var toggle = page.FindComponent<MudSwitch<bool>>();
        await page.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(false));

        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("[data-testid^=alert-formula-]").Count));
        Assert.Contains("Cleared", page.Markup);
        Assert.Equal(1, page.FindAll("[data-testid^=alert-status-]").Count(s => s.TextContent.Contains("Cleared")));
    }

    [Fact]
    public async Task Attendance_View_alone_opens_the_page_and_other_roles_are_forbidden()
    {
        var viewer = await RenderAsync("en", Permissions.Attendance.View);
        Assert.NotNull(viewer.Find("[data-testid=alerts-title]"));
        await DisposeComponentsAsync();

        await GrantAsync(Permissions.Attendance.Take); // a teacher
        RenderProviders();
        var teacher = Render<AttendanceAlertsPage>();
        teacher.WaitForAssertion(() => Assert.Empty(teacher.FindAll("[data-testid=alerts-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync("ar");

        Assert.Equal("تنبيهات الغياب", page.Find("[data-testid=alerts-title]").TextContent.Trim());
        Assert.Contains("تأخر", page.Find("[data-testid^=alert-formula-]").TextContent);
        Assert.All(page.FindAll(".lms-date-cell"), d => Assert.Equal("ltr", d.GetAttribute("dir")));
    }
}

public sealed class AttendanceReportsPageTests : StaffTestContext
{
    private readonly FakeAttendanceApi _api = new();

    public AttendanceReportsPageTests() => Services.AddSingleton<IAttendanceApi>(_api);

    private async Task<IRenderedComponent<AttendanceReportsPage>> RenderAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Attendance.View);
        RenderProviders();
        var page = Render<AttendanceReportsPage>();
        page.WaitForElement("[data-testid=reports-table]");
        return page;
    }

    [Fact]
    public async Task Reports_show_the_as_of_time_and_the_servers_numbers()
    {
        var page = await RenderAsync();

        Assert.Contains("Computed", page.Find("[data-testid=reports-asof]").TextContent); // the as-of time of the report
        Assert.Equal("84.5%", page.Find("[data-testid=reports-percent]").TextContent.Trim()); // the server's rate, not computed here
        Assert.Equal(4, page.FindAll("[data-testid^=reports-row-]").Count);
    }

    [Fact]
    public async Task Export_downloads_the_file_the_server_made()
    {
        var page = await RenderAsync();

        page.Find("[data-testid=reports-export]").Click();

        page.WaitForAssertion(() => Assert.Equal("attendance-student.xlsx", JSInterop.Invocations["lmsDownloadFile"].Single().Arguments[0]));
    }

    [Fact]
    public async Task Switching_to_by_class_asks_for_the_class_report()
    {
        var page = await RenderAsync();
        var group = page.FindComponent<MudToggleGroup<string>>();

        await page.InvokeAsync(() => group.Instance.ValueChanged.InvokeAsync("class"));
        page.Find("[data-testid=reports-export]").Click();

        page.WaitForAssertion(() => Assert.Equal("attendance-class.xlsx", JSInterop.Invocations["lmsDownloadFile"].Single().Arguments[0]));
    }

    [Fact]
    public async Task Without_Attendance_View_the_page_is_forbidden()
    {
        await GrantAsync(Permissions.Attendance.Take);
        RenderProviders();

        var page = Render<AttendanceReportsPage>();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=reports-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync("ar");

        Assert.Equal("تقارير الحضور", page.Find("[data-testid=reports-title]").TextContent.Trim());
        Assert.Contains("حُسب في", page.Find("[data-testid=reports-asof]").TextContent);
    }
}
