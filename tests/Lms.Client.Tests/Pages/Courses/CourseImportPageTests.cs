using System.Globalization;
using Bunit;
using Lms.Client.Components.Imports;
using Lms.Client.Pages.Courses;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Imports;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages.Courses;

public sealed class CourseImportPageTests : CoursesTestContext
{
    public CourseImportPageTests() => Services.AddSingleton<IImportsApi>(new StubImportsApi());

    [Fact]
    public async Task Import_page_uses_courses_kind()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View, Permissions.Courses.Import);
        RenderProviders();

        var page = Render<CourseImportPage>();

        Assert.Equal(ImportKinds.Courses, page.FindComponent<ImportWizard>().Instance.Kind);
        Assert.Contains("Import courses from Excel", page.Find("[data-testid=import-wizard]").TextContent);
    }

    [Fact]
    public async Task Import_page_is_forbidden_without_Courses_Import()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);
        RenderProviders();

        var page = Render<CourseImportPage>();

        page.WaitForElement("[data-testid=course-import-forbidden]");
        Assert.Empty(page.FindAll("[data-testid=import-wizard]"));
    }

    [Fact]
    public async Task Import_page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b => { b.OpenComponent<CourseImportPage>(0); b.CloseComponent(); })));
        // The layout resets the permissions while it loads, so they are granted once it is up.
        await layout.InvokeAsync(() => GrantAsync(Permissions.Courses.View, Permissions.Courses.Import));

        layout.WaitForElement("[data-testid=import-wizard]", TimeSpan.FromSeconds(5));
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("استيراد المقررات من Excel", layout.Find("[data-testid=import-wizard]").TextContent);
    }
}
