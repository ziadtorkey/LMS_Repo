using System.Globalization;
using Bunit;
using Lms.Client.Pages.Courses;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;

namespace Lms.Client.Tests.Pages.Courses;

public sealed class CourseListPageTests : CoursesTestContext
{
    private async Task<IRenderedComponent<CourseListPage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Courses.View] : permissions);
        RenderProviders();
        var page = Render<CourseListPage>();
        page.WaitForAssertion(() => Assert.Contains("MATH101", page.Markup));
        return page;
    }

    private Guid IdOf(string code) => Courses.All.First(c => c.Summary.Code == code).Summary.Id;

    [Fact]
    public async Task List_shows_the_courses_with_their_status_and_allocations()
    {
        var page = await RenderPageAsync();

        Assert.Contains("Mathematics", page.Markup);
        Assert.Contains("Inactive", page.Markup);
        Assert.Empty(page.FindAll($"[data-testid=inherited-chip-{IdOf("MATH101")}]"));
    }

    [Fact]
    public async Task List_marks_inherited_courses_read_only()
    {
        var page = await RenderPageAsync(Permissions.Courses.View, Permissions.Courses.Update);
        Assert.NotNull(page.Find($"[data-testid=inherited-chip-{IdOf("ARB101")}]"));

        _ = page.Find($"[data-testid=course-link-{IdOf("ARB101")}]").ClickAsync(new());

        Dialogs.WaitForElement("[data-testid=course-readonly]");
        Assert.Empty(Dialogs.FindAll("[data-testid=dialog-save]"));
    }

    [Fact]
    public async Task Own_courses_open_editable_for_callers_with_update()
    {
        var page = await RenderPageAsync(Permissions.Courses.View, Permissions.Courses.Update);

        _ = page.Find($"[data-testid=course-link-{IdOf("MATH101")}]").ClickAsync(new());

        Dialogs.WaitForElement("[data-testid=dialog-save]");
        Assert.Empty(Dialogs.FindAll("[data-testid=course-readonly]"));
    }

    [Fact]
    public async Task Own_courses_are_read_only_without_update_permission()
    {
        var page = await RenderPageAsync();

        _ = page.Find($"[data-testid=course-link-{IdOf("MATH101")}]").ClickAsync(new());

        Dialogs.WaitForElement("[data-testid=course-readonly]");
        Assert.Empty(Dialogs.FindAll("[data-testid=dialog-save]"));
    }

    [Fact]
    public async Task New_button_hidden_without_Courses_Create()
    {
        var page = await RenderPageAsync();

        Assert.Empty(page.FindAll("[data-testid=course-new]"));
        Assert.Empty(page.FindAll("[data-testid=course-import]"));
    }

    [Fact]
    public async Task New_and_import_buttons_follow_their_permissions()
    {
        var page = await RenderPageAsync(Permissions.Courses.View, Permissions.Courses.Create, Permissions.Courses.Import);

        Assert.NotNull(page.Find("[data-testid=course-new]"));
        Assert.Contains("courses/import", page.Find("[data-testid=course-import]").GetAttribute("href"));
    }

    [Fact]
    public async Task Status_filter_is_sent_to_the_api()
    {
        var page = await RenderPageAsync();

        await page.InvokeAsync(() => page.FindComponent<MudBlazor.MudSelect<string>>().Instance.ValueChanged.InvokeAsync(CourseStatuses.Inactive));

        page.WaitForAssertion(() => Assert.Equal(CourseStatuses.Inactive, Courses.LastQuery!.Status));
        page.WaitForAssertion(() => Assert.DoesNotContain("MATH101", page.Markup));
        Assert.Contains("SCI101", page.Markup);
    }

    [Fact]
    public async Task Supervised_by_me_filter_is_sent_to_the_api()
    {
        var page = await RenderPageAsync();

        await page.InvokeAsync(() => page.FindComponent<MudBlazor.MudCheckBox<bool>>().Instance.ValueChanged.InvokeAsync(true));

        page.WaitForAssertion(() => Assert.True(Courses.LastQuery!.SupervisedByMe));
    }

    [Theory]
    [InlineData("ar", "المقررات", "rtl")]
    [InlineData("en", "Courses", "ltr")]
    public async Task Pages_render_in_the_active_direction(string culture, string title, string dir)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Courses.View);

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b => { b.OpenComponent<CourseListPage>(0); b.CloseComponent(); })));

        layout.WaitForAssertion(() => Assert.Contains("MATH101", layout.Markup));
        Assert.Equal(dir, layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains(title, layout.Find("[data-testid=course-title]").TextContent);
    }

    [Fact]
    public async Task Course_codes_are_left_to_right()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Courses.View);
        var page = Render<CourseListPage>();
        page.WaitForAssertion(() => Assert.Contains("MATH101", page.Markup));

        Assert.Contains(page.FindAll("span[dir=ltr]"), s => s.TextContent == "MATH101");
    }
}
