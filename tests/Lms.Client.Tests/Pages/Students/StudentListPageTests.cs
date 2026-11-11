using Lms.Client.Services.Api;
using System.Globalization;
using Bunit;
using Lms.Client.Pages.Students;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Students;

public sealed class StudentListPageTests : StudentsTestContext
{
    private async Task<IRenderedComponent<StudentListPage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Students.View] : permissions);
        RenderProviders();
        var page = Render<StudentListPage>();
        page.WaitForAssertion(() => Assert.Contains("S-1001", page.Markup));
        return page;
    }

    [Fact]
    public async Task List_shows_code_name_unit_primary_guardian_and_status()
    {
        var page = await RenderPageAsync();

        Assert.Contains("Ahmed Saeed", page.Markup);
        Assert.Contains("Main School", page.Markup);
        Assert.Contains("Mona", page.Markup);
        Assert.Contains("0501111111", page.Markup);
        Assert.Contains("2014-03-05", page.Markup);
        Assert.Contains("Deactivated", page.Markup);
    }

    [Fact]
    public async Task List_shows_the_class_or_an_unplaced_chip()
    {
        var page = await RenderPageAsync();
        var ahmed = Students.All.First(s => s.Summary.NameEn == "Ahmed Saeed").Summary.Id;
        var laila = Students.All.First(s => s.Summary.NameEn == "Laila Hassan").Summary.Id;

        Assert.Contains("Grade 1 A", page.Find($"[data-testid=student-class-{ahmed}]").TextContent);
        Assert.NotNull(page.Find($"[data-testid=unplaced-chip-{laila}]"));
    }

    [Fact]
    public async Task Filters_send_query()
    {
        var page = await RenderPageAsync();

        await page.InvokeAsync(() => StringSelect(page, "student-filter-status").Instance.ValueChanged.InvokeAsync(PersonStatuses.Deactivated));
        page.WaitForAssertion(() => Assert.Equal(PersonStatuses.Deactivated, Students.LastQuery!.Status));
        await page.InvokeAsync(() => StringSelect(page, "student-filter-gender").Instance.ValueChanged.InvokeAsync(Genders.Male));
        page.WaitForAssertion(() => Assert.Equal(Genders.Male, Students.LastQuery!.Gender));
        await page.InvokeAsync(() => page.FindComponent<MudCheckBox<bool>>().Instance.ValueChanged.InvokeAsync(true));
        page.WaitForAssertion(() => Assert.True(Students.LastQuery!.Unplaced));
    }

    [Fact]
    public async Task Unit_filter_sends_the_unit()
    {
        var page = await RenderPageAsync();

        await page.InvokeAsync(() => page.FindComponent<Lms.Client.Components.People.UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));

        page.WaitForAssertion(() => Assert.Equal(FakeUnits.SchoolId, Students.LastQuery!.OrgUnitId));
    }

    [Fact]
    public async Task New_button_is_hidden_without_Students_Create()
    {
        var page = await RenderPageAsync();

        Assert.Empty(page.FindAll("[data-testid=student-new]"));
    }

    [Fact]
    public async Task New_button_links_to_the_create_page_with_Students_Create()
    {
        var page = await RenderPageAsync(Permissions.Students.View, Permissions.Students.Create);

        Assert.Contains("students/new", page.Find("[data-testid=student-new]").GetAttribute("href"));
    }

    [Theory]
    [InlineData("ar", "الطلاب", "rtl")]
    [InlineData("en", "Students", "ltr")]
    public async Task Pages_render_in_the_active_direction(string culture, string title, string dir)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Students.View);

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b => { b.OpenComponent<StudentListPage>(0); b.CloseComponent(); })));

        layout.WaitForAssertion(() => Assert.Contains("S-1001", layout.Markup));
        Assert.Equal(dir, layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains(title, layout.Find("[data-testid=students-title]").TextContent);
        Assert.Contains(layout.FindAll("span[dir=ltr]"), s => s.TextContent == "S-1001");
    }
}
