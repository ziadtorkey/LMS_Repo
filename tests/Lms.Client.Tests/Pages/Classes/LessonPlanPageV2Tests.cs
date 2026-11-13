using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages.Classes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;

namespace Lms.Client.Tests.Pages.Classes;

/// <summary>Plan view v2 (M4a T15): modules under lessons, visibility chips for staff, completion state and progress for students.</summary>
public sealed class LessonPlanPageV2Tests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private async Task<IRenderedComponent<LessonPlanPage>> OpenAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.LessonPlans.View);
        RenderProviders();
        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));
        page.WaitForElement("[data-testid=plan-header]", Wait);
        return page;
    }

    private static string Module(Guid id) => $"[data-testid=module-{id}]";

    [Fact]
    public async Task Student_sees_only_modules_the_server_sends()
    {
        Plans.HideHiddenItems = true;
        var page = await OpenAsync();

        Assert.NotEmpty(page.FindAll(Module(FakeLessonPlansApi.PageModuleId)));
        Assert.NotEmpty(page.FindAll(Module(FakeLessonPlansApi.VideoModuleId)));
        Assert.Empty(page.FindAll(Module(FakeLessonPlansApi.HiddenQuizModuleId)));
        Assert.Empty(page.FindAll(Module(FakeLessonPlansApi.GroupLinkModuleId)));
        // students get completion states, never staff chips
        Assert.Equal("Completed", page.Find(Module(FakeLessonPlansApi.PageModuleId) + " [data-testid=module-state]").GetAttribute("data-state"));
        Assert.Equal("NotStarted", page.Find(Module(FakeLessonPlansApi.VideoModuleId) + " [data-testid=module-state]").GetAttribute("data-state"));
        Assert.Empty(page.FindAll("[data-testid^=module-chip-]"));
    }

    [Fact]
    public async Task Teacher_sees_hidden_and_scheduled_chips()
    {
        var page = await OpenAsync();

        Assert.Equal(4, page.FindAll("[data-testid=plan-modules-00000000-0000-0000-0000-0000000000b1] [data-testid^=module-0]").Count);
        Assert.Contains("Hidden", page.Find(Module(FakeLessonPlansApi.HiddenQuizModuleId) + " [data-testid=module-chip-hidden]").TextContent);
        var scheduled = page.Find(Module(FakeLessonPlansApi.GroupLinkModuleId));
        Assert.Contains("2099-01-01", scheduled.QuerySelector("[data-testid=module-chip-scheduled]")!.TextContent);
        Assert.Contains("Group A", scheduled.QuerySelector("[data-testid=module-chip-group]")!.TextContent);
        Assert.NotNull(scheduled.QuerySelector("[data-testid=module-chip-notvisible]"));
        Assert.Contains("lms-plan-hidden", scheduled.ClassName);
        Assert.Empty(page.FindAll("[data-testid=module-state]"));
        Assert.Empty(page.FindAll("[data-testid=course-progress]"));
    }

    [Fact]
    public async Task Progress_bar_reflects_completed_over_total()
    {
        Plans.HideHiddenItems = true;
        var page = await OpenAsync();

        Assert.Contains("1 of 3", page.Find("[data-testid=course-progress-count]").TextContent);
        var lesson = page.Find("[data-testid=lesson-progress-00000000-0000-0000-0000-0000000000b1]");
        Assert.Contains("1 of 2", lesson.TextContent);
        var bar = page.Find("[data-testid=course-progress] [role=progressbar]");
        Assert.Equal("33", bar.GetAttribute("aria-valuenow"));
    }

    [Fact]
    public async Task Lessons_without_counted_modules_show_no_progress_bar()
    {
        Plans.HideHiddenItems = true;
        var page = await OpenAsync();

        // lesson "First words" has no modules at all
        Assert.Empty(page.FindAll("[data-testid=lesson-progress-00000000-0000-0000-0000-0000000000b2]"));
    }

    [Fact]
    public async Task Module_titles_link_to_the_module_viewer_and_follow_the_ui_language()
    {
        var page = await OpenAsync();

        var link = page.Find(Module(FakeLessonPlansApi.PageModuleId) + " [data-testid=module-title]");
        Assert.Equal($"/modules/{FakeLessonPlansApi.PageModuleId}", link.GetAttribute("href"));
        Assert.Equal("The letters story", link.TextContent.Trim());
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        Plans.HideHiddenItems = true;
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.LessonPlans.View);
        PlaceContextAtUnit();
        var page = Render<MainLayout>(p => p.Add(l => l.Body, b => { b.OpenComponent<LessonPlanPage>(0); b.AddAttribute(1, nameof(LessonPlanPage.Id), FakeLessonPlansApi.ArabicClassCourseId); b.CloseComponent(); }));
        page.WaitForElement("[data-testid=plan-header]", Wait);

        Assert.Equal("rtl", page.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("قصة الحروف", page.Find(Module(FakeLessonPlansApi.PageModuleId)).TextContent);
        Assert.Contains("1 من 3", page.Find("[data-testid=course-progress-count]").TextContent);
        Assert.Equal("مكتمل", page.Find(Module(FakeLessonPlansApi.PageModuleId) + " [data-testid=module-state]").GetAttribute("aria-label"));
    }
}
