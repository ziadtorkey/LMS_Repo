using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Lms.Client.Pages.Forums;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;

namespace Lms.Client.Tests.Pages.Forums;

/// <summary>The class course tabs depend on the caller's permissions (M5 Task 22: the gradebook tab).</summary>
public sealed class ClassCourseTabsTests : ClientTestContext
{
    private async Task<IRenderedComponent<ClassCourseTabs>> RenderTabsAsync(string active, params string[] permissions)
    {
        await GrantAsync(permissions);
        await Services.GetRequiredService<Lms.Client.Services.PermissionState>().RefreshAsync();
        return Render<ClassCourseTabs>(p => p.Add(c => c.ClassCourseId, Guid.NewGuid()).Add(c => c.Active, active));
    }

    [Fact]
    public async Task The_plan_tab_is_always_there_and_the_others_need_their_permission()
    {
        var tabs = await RenderTabsAsync("plan");

        Assert.NotEmpty(tabs.FindAll("[data-testid=tab-plan]"));
        Assert.Empty(tabs.FindAll("[data-testid=tab-forum]"));
        Assert.Empty(tabs.FindAll("[data-testid=tab-announcements]"));
        Assert.Empty(tabs.FindAll("[data-testid=tab-gradebook]"));
    }

    [Fact]
    public async Task The_gradebook_tab_needs_Grades_View_and_only_that()
    {
        var tabs = await RenderTabsAsync("plan", Permissions.Grades.View);

        Assert.NotEmpty(tabs.FindAll("[data-testid=tab-gradebook]"));
        Assert.EndsWith("/gradebook", tabs.Find("[data-testid=tab-gradebook]").GetAttribute("href"));
        Assert.Empty(tabs.FindAll("[data-testid=tab-forum]"));
    }

    [Fact]
    public async Task The_forum_tabs_need_Forums_View_and_not_Grades_View()
    {
        var tabs = await RenderTabsAsync("forum", Permissions.Forums.View);

        Assert.NotEmpty(tabs.FindAll("[data-testid=tab-forum]"));
        Assert.NotEmpty(tabs.FindAll("[data-testid=tab-announcements]"));
        Assert.Empty(tabs.FindAll("[data-testid=tab-gradebook]"));
    }
}
