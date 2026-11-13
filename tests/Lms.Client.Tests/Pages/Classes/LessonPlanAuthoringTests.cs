using System.Globalization;
using Bunit;
using Lms.Client.Pages.Classes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;

namespace Lms.Client.Tests.Pages.Classes;

/// <summary>The plan page mounts the module authoring components (#389) for staff with Modules.Manage.</summary>
public sealed class LessonPlanAuthoringTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Lesson1 = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid RetiredLesson = Guid.Parse("00000000-0000-0000-0000-0000000000b3");

    private async Task<IRenderedComponent<LessonPlanPage>> OpenAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));
        page.WaitForElement("[data-testid=plan-header]", Wait);
        return page;
    }

    [Fact]
    public async Task Staff_with_Modules_Manage_get_add_menus_and_module_controls()
    {
        var page = await OpenAsync(Permissions.LessonPlans.View, Permissions.Modules.Manage);

        // one add menu per editable lesson (the two retired lessons are history only)
        Assert.Equal(3, page.FindAll("[data-testid=module-add-menu]").Count);
        Assert.NotEmpty(page.FindAll($"[data-testid=module-controls-{FakeLessonPlansApi.PageModuleId}]"));
        Assert.NotEmpty(page.FindAll($"[data-testid=module-edit-{FakeLessonPlansApi.PageModuleId}]"));
    }

    [Fact]
    public async Task Staff_without_Modules_Manage_see_the_modules_but_no_authoring_controls()
    {
        var page = await OpenAsync(Permissions.LessonPlans.View);

        Assert.NotEmpty(page.FindAll($"[data-testid=module-{FakeLessonPlansApi.PageModuleId}]"));
        Assert.Empty(page.FindAll("[data-testid=module-add-menu]"));
        Assert.Empty(page.FindAll("[data-testid^=module-controls-]"));
    }

    [Fact]
    public async Task Students_never_get_authoring_controls()
    {
        Plans.HideHiddenItems = true;
        var page = await OpenAsync(Permissions.LessonPlans.View, Permissions.Modules.Manage);

        Assert.Empty(page.FindAll("[data-testid=module-add-menu]"));
        Assert.Empty(page.FindAll("[data-testid^=module-controls-]"));
    }

    [Fact]
    public async Task The_plan_reloads_after_the_authoring_components_report_a_change()
    {
        var page = await OpenAsync(Permissions.LessonPlans.View, Permissions.Modules.Manage);
        Assert.Empty(page.FindAll("[data-testid^=module-controls-" + "00000000-0000-0000-0000-0000000000f1]"));
        var plan = Plans.Plans[FakeLessonPlansApi.ArabicClassCourseId];
        var added = new ModuleNodeDto(Guid.Parse("00000000-0000-0000-0000-0000000000f1"), ModuleKinds.Link, 9, "جديد", "Brand new", ModuleVisibilities.Published,
            null, null, ModuleTargets.ClassCourse, null, null, null, CompletionRules.None, true, null);
        Plans.Plans[FakeLessonPlansApi.ArabicClassCourseId] = plan with
        {
            Sections = plan.Sections.Select(s => s with
            {
                Lessons = s.Lessons.Select(l => l.Key == Lesson1 ? l with { Modules = [.. l.Modules ?? [], added] } : l).ToList(),
            }).ToList(),
        };

        await page.InvokeAsync(() => page.FindComponent<LessonPlanTree>().Instance.OnChanged.InvokeAsync());

        page.WaitForAssertion(() => Assert.Contains("Brand new", page.Markup), Wait);
    }
}
