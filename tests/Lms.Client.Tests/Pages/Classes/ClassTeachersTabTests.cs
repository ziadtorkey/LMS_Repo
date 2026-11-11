using System.Globalization;
using Bunit;
using Lms.Client.Pages.Classes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Classes;
using Lms.Shared.Teaching;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Classes;

public sealed class ClassTeachersTabTests : ClassesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeClassCoursesApi _classCourses = new();
    private readonly FakeTeachingApi _teaching = new();

    public ClassTeachersTabTests()
    {
        Services.AddSingleton<IClassCoursesApi>(_classCourses);
        Services.AddSingleton<ITeachingApi>(_teaching);
    }

    private static ClassSummaryDto Class => FakeClasses.Grade1A;

    private ClassCourseDto Math => _classCourses.All.First(c => c.ClassId == Class.Id && c.CourseCode == "MATH101");

    private IRenderedComponent<ClassTeachersTab> RenderTab(bool manage = true, ClassSummaryDto? cls = null)
    {
        Culture.Current = new CultureInfo("en");
        RenderProviders();
        return Render<ClassTeachersTab>(p => p.Add(t => t.Class, cls ?? Class).Add(t => t.CanManage, manage));
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public void Tab_shows_each_course_with_its_current_teachers()
    {
        _teaching.Seed(Math.Id, FakeTeachingApi.Sara, TeacherRoles.Primary, Today.AddDays(-30));
        _teaching.Seed(Math.Id, FakeTeachingApi.Omar, TeacherRoles.Assistant, Today.AddDays(-10));
        var cut = RenderTab();

        var section = cut.WaitForElement("[data-testid=teachers-section-MATH101]");
        Assert.Contains("Sara Teacher", section.TextContent);
        Assert.Contains("Primary", section.TextContent);
        Assert.Contains("Omar Teacher", section.TextContent);
        Assert.Contains("Assistant", section.TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=teachers-none-SCI101]")); // Science has nobody yet
    }

    [Fact]
    public async Task Assign_dialog_lists_eligible_teachers_only()
    {
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=teacher-assign-MATH101]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=assign-teacher]");

        var select = Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "assign-teacher");
        var offered = select.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Instance.Value).ToList();

        // Sara and Omar are eligible for Mathematics; Noor is only eligible for Science.
        Assert.Contains((Guid?)FakeTeachingApi.Sara, offered);
        Assert.Contains((Guid?)FakeTeachingApi.Omar, offered);
        Assert.DoesNotContain((Guid?)FakeTeachingApi.Noor, offered);
    }

    [Fact]
    public async Task Assign_sends_teacher_role_and_date()
    {
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=teacher-assign-MATH101]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=assign-teacher]");
        var select = Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "assign-teacher");
        await Dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(FakeTeachingApi.Sara));

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_teaching.Assigns), Wait);
        Assert.Equal((Math.Id, FakeTeachingApi.Sara, TeacherRoles.Primary), (_teaching.Assigns[0].ClassCourseId, _teaching.Assigns[0].Request.UserId, _teaching.Assigns[0].Request.Role));
        Assert.Equal(Today, _teaching.Assigns[0].Request.From);
        await cut.WaitForAssertionAsync(() => Assert.Contains("Sara Teacher", cut.Find("[data-testid=teachers-section-MATH101]").TextContent), Wait);
    }

    [Fact]
    public async Task Second_primary_shows_replace_hint()
    {
        _teaching.Seed(Math.Id, FakeTeachingApi.Sara, TeacherRoles.Primary, Today.AddDays(-30));
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=teacher-assign-MATH101]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=assign-teacher]");
        var select = Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "assign-teacher");
        await Dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(FakeTeachingApi.Omar)); // Primary is the default role

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Assert.Contains("Use Replace on the current primary teacher", (await Dialogs.WaitForElementAsync("[data-testid=replace-hint]", Wait)).TextContent);
        Assert.Single(_teaching.Assigns); // the attempt was made, nothing was added
        Assert.DoesNotContain("Omar Teacher", cut.Find("[data-testid=teachers-section-MATH101]").TextContent);
    }

    [Fact]
    public async Task Replace_sends_effective_date_and_history_shows_both()
    {
        var current = _teaching.Seed(Math.Id, FakeTeachingApi.Sara, TeacherRoles.Primary, Today.AddDays(-60));
        var cut = RenderTab();
        _ = cut.WaitForElement($"[data-testid=teacher-replace-{current.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=replace-teacher]");
        var select = Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "replace-teacher");
        Assert.DoesNotContain((Guid?)FakeTeachingApi.Sara, select.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Instance.Value)); // not the teacher being replaced
        await Dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(FakeTeachingApi.Omar));

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_teaching.Replaces), Wait);
        Assert.Equal((Math.Id, current.Id, FakeTeachingApi.Omar, Today), (_teaching.Replaces[0].ClassCourseId, _teaching.Replaces[0].AssignmentId,
            _teaching.Replaces[0].Request.NewUserId, _teaching.Replaces[0].Request.EffectiveOn));

        // Without history only the new current teacher is listed; with it, both.
        await cut.WaitForAssertionAsync(() => Assert.DoesNotContain("Sara Teacher", cut.Find("[data-testid=teachers-section-MATH101]").TextContent), Wait);
        await cut.Find("input[data-testid=teachers-history]").ChangeAsync(new() { Value = true });
        await cut.WaitForAssertionAsync(() =>
        {
            var text = cut.Find("[data-testid=teachers-section-MATH101]").TextContent;
            Assert.Contains("Sara Teacher", text);
            Assert.Contains("Omar Teacher", text);
            Assert.Contains(Today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), text); // Sara ended the day before
        }, Wait);
    }

    [Fact]
    public async Task End_sends_the_last_day()
    {
        var current = _teaching.Seed(Math.Id, FakeTeachingApi.Omar, TeacherRoles.Assistant, Today.AddDays(-5));
        var cut = RenderTab();
        _ = cut.WaitForElement($"[data-testid=teacher-end-{current.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=end-confirm]");

        await Dialogs.Find("[data-testid=end-confirm]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_teaching.Ends), Wait);
        Assert.Equal((Math.Id, current.Id, Today), (_teaching.Ends[0].ClassCourseId, _teaching.Ends[0].AssignmentId, _teaching.Ends[0].Request.LastDay));
    }

    [Fact]
    public void Actions_hidden_without_manage_and_in_a_closed_class()
    {
        var current = _teaching.Seed(Math.Id, FakeTeachingApi.Sara, TeacherRoles.Primary, Today.AddDays(-30));
        var viewer = RenderTab(manage: false);
        viewer.WaitForElement("[data-testid=teachers-section-MATH101]");
        Assert.Empty(viewer.FindAll("[data-testid=teacher-assign-MATH101]"));
        Assert.Empty(viewer.FindAll($"[data-testid=teacher-replace-{current.Id}]"));

        var closed = Render<ClassTeachersTab>(p => p.Add(t => t.Class, Class with { IsReadOnly = true }).Add(t => t.CanManage, true));
        closed.WaitForElement("[data-testid=teachers-section-MATH101]");
        Assert.Empty(closed.FindAll("[data-testid=teacher-assign-MATH101]"));
        Assert.Empty(closed.FindAll($"[data-testid=teacher-end-{current.Id}]"));
    }

    [Fact]
    public void Tab_renders_rtl_in_arabic()
    {
        _teaching.Seed(Math.Id, FakeTeachingApi.Sara, TeacherRoles.Primary, Today.AddDays(-30));
        Culture.Current = new CultureInfo("ar");
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<ClassTeachersTab>(c => c.Add(t => t.Class, Class).Add(t => t.CanManage, true)));

        var section = rtl.WaitForElement("[data-testid=teachers-section-MATH101]");
        Assert.Contains("سارة المعلمة", section.TextContent);
        Assert.Contains("أساسي", section.TextContent);
        Assert.Contains("تعيين معلم", section.TextContent);
        Assert.Equal("ltr", section.QuerySelector("span[dir]")!.GetAttribute("dir"));
    }
}
