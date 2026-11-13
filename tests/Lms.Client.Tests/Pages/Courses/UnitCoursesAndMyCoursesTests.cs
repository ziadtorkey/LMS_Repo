using System.Globalization;
using Bunit;
using Lms.Client.Pages.Courses;
using Lms.Client.Pages.Organization;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;

namespace Lms.Client.Tests.Pages.Courses;

public sealed class UnitCoursesAndMyCoursesTests : CoursesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Unit_tab_lists_available_courses_with_inherited_ones_marked_and_those_below()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);

        var tab = Render<UnitCoursesTab>(p => p.Add(x => x.UnitId, FakeUnits.SchoolId));

        tab.WaitForElement("[data-testid=unit-courses-available]", Wait);
        var available = tab.Find("[data-testid=unit-courses-available]").TextContent;
        Assert.Contains("MATH101", available);
        Assert.Contains("ARB101", available);
        var arabic = Courses.AllocationsOf(Courses.All.First(c => c.Summary.Code == "ARB101").Summary.Id).First();
        Assert.NotNull(tab.Find($"[data-testid=unit-course-inherited-{arabic.Id}]"));
        Assert.Contains("MATH101", tab.Find("[data-testid=unit-courses-below]").TextContent);
    }

    [Fact]
    public async Task Unit_tab_shows_empty_states()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);

        var tab = Render<UnitCoursesTab>(p => p.Add(x => x.UnitId, FakeUnits.GradeId));

        tab.WaitForElement("[data-testid=unit-courses-below-empty]", Wait);
        Assert.NotEmpty(tab.FindAll("[data-testid=unit-courses-available]"));
    }

    [Fact]
    public async Task My_courses_shows_supervised_only()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);

        var page = Render<MyCoursesPage>();

        page.WaitForElement("[data-testid=my-courses-title]");
        var math = Courses.All.First(c => c.Summary.Code == "MATH101").Summary;
        var science = Courses.All.First(c => c.Summary.Code == "SCI101").Summary;
        page.WaitForElement($"[data-testid=my-course-{math.Id}]", Wait);
        Assert.Empty(page.FindAll($"[data-testid=my-course-{science.Id}]"));
        Assert.Contains("No published blueprint", page.Find($"[data-testid=my-course-blueprint-{math.Id}]").TextContent);
        Assert.Contains($"courses/{math.Id}", page.Find($"[data-testid=my-course-open-{math.Id}]").GetAttribute("href"));
    }

    [Fact]
    public async Task My_courses_shows_a_message_when_nothing_is_supervised()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);
        var supervised = Courses.All.First(c => c.Summary.Code == "MATH101");
        await Courses.SetSupervisorsAsync(supervised.Summary.Id, new(Array.Empty<Guid>()));

        var page = Render<MyCoursesPage>();

        page.WaitForElement("[data-testid=my-courses-empty]", Wait);
    }

    [Fact]
    public async Task My_courses_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b => { b.OpenComponent<MyCoursesPage>(0); b.CloseComponent(); })));
        await layout.InvokeAsync(() => GrantAsync(Permissions.Courses.View));

        layout.WaitForElement("[data-testid=my-courses-title]", Wait);
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("مقرراتي", layout.Find("[data-testid=my-courses-title]").TextContent);
    }
}
