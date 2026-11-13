using System.Globalization;
using Bunit;
using Lms.Client.Pages.Enrollment;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Enrollment;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Enrollment;

public sealed class StudentEnrollmentsTabTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeEnrollmentApi _enrollment = new();

    public StudentEnrollmentsTabTests() => Services.AddSingleton<IEnrollmentApi>(_enrollment);

    private IRenderedComponent<StudentEnrollmentsTab> RenderTab(bool manage = true, bool transfer = true)
    {
        RenderProviders();
        return Render<StudentEnrollmentsTab>(p => p.Add(t => t.Student, Students.All[0]).Add(t => t.CanManage, manage).Add(t => t.CanTransfer, transfer));
    }

    [Fact]
    public async Task Student_tab_lists_history()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderTab();

        cut.WaitForElement("[data-testid=enrollments-table]");
        Assert.Equal("Automatic", cut.Find("[data-testid=enrollment-source-MATH1]").TextContent.Trim());
        Assert.Equal("Manual", cut.Find("[data-testid=enrollment-source-MUS1]").TextContent.Trim());
        Assert.Equal("Exception", cut.Find("[data-testid=enrollment-source-ART1]").TextContent.Trim());
        var dropped = cut.Find("[data-testid=enrollment-status-ART1]").TextContent;
        Assert.Contains("Dropped", dropped);
        Assert.Contains("Schedule clash", dropped);
        var transfers = cut.Find("[data-testid=transfers-table]").TextContent;
        Assert.Contains("Grade 1 B", transfers);
        Assert.Contains("Grade 1 A", transfers);
        Assert.Contains("Parent request", transfers);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Drop_asks_for_a_reason_and_sends_it()
    {
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=enrollment-drop-MUS1]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=reason-confirm]");
        Dialogs.Find("textarea[data-testid=reason-text]").Input("Too many courses");

        await Dialogs.Find("[data-testid=reason-confirm]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_enrollment.Drops), Wait);
        Assert.Equal("Too many courses", _enrollment.Drops[0].Request.Reason);
        await cut.WaitForAssertionAsync(() => Assert.Contains("Too many courses", cut.Find("[data-testid=enrollment-status-MUS1]").TextContent), Wait);
    }

    [Fact]
    public async Task Drop_only_for_active_enrollments_with_Enrollment_Manage()
    {
        var manager = RenderTab();
        manager.WaitForElement("[data-testid=enrollments-table]");
        Assert.NotEmpty(manager.FindAll("[data-testid=enrollment-drop-MATH1]"));
        Assert.Empty(manager.FindAll("[data-testid=enrollment-drop-ART1]")); // already dropped

        var viewer = Render<StudentEnrollmentsTab>(p => p.Add(t => t.Student, Students.All[0]).Add(t => t.CanManage, false).Add(t => t.CanTransfer, false));
        viewer.WaitForElement("[data-testid=enrollments-table]");
        Assert.Empty(viewer.FindAll("[data-testid=enrollment-drop-MATH1]"));
        Assert.Empty(viewer.FindAll("[data-testid=student-transfer]"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Tab_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true)
            .AddChildContent<StudentEnrollmentsTab>(c => c.Add(t => t.Student, Students.All[0]).Add(t => t.CanManage, true)));

        rtl.WaitForElement("[data-testid=enrollments-table]");
        Assert.Contains("الرياضيات", rtl.Markup);
        Assert.Equal("تلقائي", rtl.Find("[data-testid=enrollment-source-MATH1]").TextContent.Trim());
        Assert.Contains("عمليات النقل", rtl.Markup);
        Assert.Equal("ltr", rtl.Find("[data-testid=enrollments-table] span[dir]").GetAttribute("dir"));
        await Task.CompletedTask;
    }
}
