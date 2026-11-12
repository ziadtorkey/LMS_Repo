using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Classes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.People;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Classes;

public sealed class ClassListPageTests : ClassesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static string[] Admin => [Permissions.Classes.View, Permissions.Classes.Create, Permissions.Classes.Update, Permissions.Classes.Close];

    private async Task<IRenderedComponent<ClassListPage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Classes.View] : permissions);
        RenderProviders();
        var page = Render<ClassListPage>();
        page.WaitForAssertion(() => Assert.Contains("Grade 1 A", page.Markup), Wait);
        return page;
    }

    [Fact]
    public async Task List_uses_selected_year_and_shows_columns()
    {
        var page = await RenderPageAsync();

        Assert.Equal(YearId, ClassesApi.LastQuery!.AcademicYearId);
        Assert.Contains("2026/2027", page.Find("[data-testid=classes-year]").TextContent);
        Assert.Contains("12 / 30", page.Find($"[data-testid=class-students-{FakeClasses.Grade1A.Id}]").TextContent);
        Assert.Contains("Main School", page.Markup);
    }

    [Fact]
    public async Task Full_class_is_flagged_with_the_warning_colour()
    {
        var page = await RenderPageAsync();

        Assert.Contains("mud-warning-text", page.Find($"[data-testid=class-students-{FakeClasses.Grade1B.Id}]").ClassName);
        Assert.DoesNotContain("mud-warning-text", page.Find($"[data-testid=class-students-{FakeClasses.Grade1A.Id}]").ClassName);
    }

    [Fact]
    public async Task Changing_the_year_reloads_the_list_for_that_year()
    {
        var page = await RenderPageAsync();

        await page.InvokeAsync(() => Year.SelectAsync(NextYearId));

        page.WaitForAssertion(() => Assert.Equal(NextYearId, ClassesApi.LastQuery!.AcademicYearId), Wait);
    }

    [Fact]
    public async Task Closed_year_is_read_only()
    {
        Year.Close();

        var page = await RenderPageAsync(Admin);

        Assert.NotNull(page.Find("[data-testid=classes-readonly]"));
        Assert.Empty(page.FindAll("[data-testid=class-new]"));
        Assert.Empty(page.FindAll("[data-testid=class-bulk]"));
        Assert.Empty(page.FindAll("[data-testid=class-copy]"));
        Assert.Empty(page.FindAll($"[data-testid=class-edit-{FakeClasses.Grade1A.Id}]"));
    }

    [Fact]
    public async Task Buttons_follow_permissions()
    {
        var viewer = await RenderPageAsync();
        Assert.Empty(viewer.FindAll("[data-testid=class-new]"));
        Assert.Empty(viewer.FindAll($"[data-testid=class-close-{FakeClasses.Grade1A.Id}]"));
    }

    [Fact]
    public async Task Admin_sees_create_bulk_copy_edit_and_close()
    {
        var page = await RenderPageAsync(Admin);

        Assert.NotNull(page.Find("[data-testid=class-new]"));
        Assert.NotNull(page.Find("[data-testid=class-bulk]"));
        Assert.NotNull(page.Find("[data-testid=class-copy]"));
        Assert.NotNull(page.Find($"[data-testid=class-edit-{FakeClasses.Grade1A.Id}]"));
        Assert.NotNull(page.Find($"[data-testid=class-close-{FakeClasses.Grade1A.Id}]"));
    }

    [Fact]
    public async Task Close_asks_for_confirmation_then_closes()
    {
        var page = await RenderPageAsync(Admin);

        _ = page.Find($"[data-testid=class-close-{FakeClasses.Grade1A.Id}]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]", Wait).ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains(FakeClasses.Grade1A.Id, ClassesApi.Closed), Wait);
        page.WaitForAssertion(() => Assert.Contains("Closed", page.Markup), Wait);
    }

    [Fact]
    public async Task Cancelling_the_confirmation_does_not_close()
    {
        var page = await RenderPageAsync(Admin);

        _ = page.Find($"[data-testid=class-close-{FakeClasses.Grade1A.Id}]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-no]", Wait).ClickAsync(new());

        Assert.Empty(ClassesApi.Closed);
    }

    [Fact]
    public async Task Filters_send_query()
    {
        var page = await RenderPageAsync();

        await page.InvokeAsync(() => StringSelect(page, "class-filter-status").Instance.ValueChanged.InvokeAsync(ClassStatuses.Closed));
        page.WaitForAssertion(() => Assert.Equal(ClassStatuses.Closed, ClassesApi.LastQuery!.Status), Wait);
        await page.InvokeAsync(() => page.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        page.WaitForAssertion(() => Assert.Equal(FakeUnits.SchoolId, ClassesApi.LastQuery!.OrgUnitId), Wait);
    }

    [Theory]
    [InlineData("ar", "الفصول", "rtl")]
    [InlineData("en", "Classes", "ltr")]
    public async Task Pages_render_in_the_active_direction(string culture, string title, string dir)
    {
        Culture.Current = new CultureInfo(culture);

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b => { b.OpenComponent<ClassListPage>(0); b.CloseComponent(); })));
        await layout.InvokeAsync(() => GrantAsync(Permissions.Classes.View));

        layout.WaitForAssertion(() => Assert.Contains("1A", layout.Markup), Wait);
        Assert.Equal(dir, layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains(title, layout.Find("[data-testid=classes-title]").TextContent);
        Assert.Contains(layout.FindAll("span[dir=ltr]"), s => s.TextContent == "1A");
    }
}
