using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// M4a exit demo 1, the student side (plan Task 31, #381): what a student sees of a plan with scheduled, hidden and group modules,
/// opening content, marking it complete, progress and "continue". The modules are created through the API; the student is a real
/// sign-in (first sign-in goes through the forced password change, D24).
/// </summary>
public sealed class ContentStudentTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>Un-skip when the M4a modules (#391, #384, #401) and the student pages (#390, #394) are on main.</summary>
    private const string NeedsM4a = "The M4a APIs and pages are on main; run once on the shared stack, then unskip";

    private static readonly byte[] Pdf = "%PDF-1.7\n%probe\n"u8.ToArray();

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Scheduled_and_group_modules_are_hidden_from_other_students(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var (world, ids) = await SetUpAsync(async (api, w) =>
        {
            var ids = new Dictionary<string, Guid>
            {
                ["pub"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "Open to all", html: "<p>all</p>"),
                ["a"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "Group A only", groupId: w.Group1Id, html: "<p>a</p>"),
                ["b"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "Group B only", groupId: w.Group2Id, html: "<p>b</p>"),
                ["later"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "Opens next week", visibility: "Scheduled",
                    from: DateTime.UtcNow.AddDays(7), to: DateTime.UtcNow.AddDays(14), html: "<p>later</p>"),
                ["hidden"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "Hidden by the teacher", visibility: "Hidden", html: "<p>h</p>"),
            };
            return ids;
        });

        // Student 1 (group A) sees the open module and group A's, nothing else.
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var course = new MyCoursePage(Page);
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ModuleLink(ids["pub"])).ToBeVisibleAsync();
        await Expect(course.ModuleLink(ids["a"])).ToBeVisibleAsync();
        await Expect(course.ModuleLinks).ToHaveCountAsync(2);
        foreach (var hiddenFromA in new[] { "b", "later", "hidden" })
        {
            await Expect(course.Module(ids[hiddenFromA])).ToHaveCountAsync(0);
        }

        // Opening a module that is not theirs by its address answers "not found", never the content.
        var viewer = new ModuleViewerPage(Page);
        foreach (var hiddenFromA in new[] { "b", "later", "hidden" })
        {
            await viewer.OpenAsync(world.ClassCourseId, ids[hiddenFromA]);
            await Expect(Page.GetByText(Resx.In(culture, "Error_not_found"))).ToBeVisibleAsync(new() { Timeout = 40_000 });
            await Expect(viewer.Title).ToHaveCountAsync(0);
            var status = (await new E2EApi(Page, Settings.BaseUrl).SendAsync("GET", $"/api/modules/{ids[hiddenFromA]}")).Status;
            Assert.Equal(404, status);
        }

        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_opens_page_marks_file_complete_and_progress_updates(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var (world, ids) = await SetUpAsync(async (api, w) =>
        {
            var asset = await M4Kit.UploadAssetAsync(new E2EApi(Page, Settings.BaseUrl), w.ClassCourseId, "worksheet.pdf", "application/pdf", Pdf);
            return new Dictionary<string, Guid>
            {
                ["page"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "Read this page", rule: "View", html: "<p>Hello</p>"),
                ["file"] = await M4Kit.AddModuleAsync(api, w, 0, "File", "Worksheet", rule: "Manual", assetId: asset),
            };
        });

        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var course = new MyCoursePage(Page);
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ProgressText).ToContainTextAsync("0");
        await Expect(course.ModuleState(ids["page"])).ToContainTextAsync(Resx.In(culture, "CompletionState_NotStarted"));

        // Opening a View module completes it.
        await course.ModuleLink(ids["page"]).ClickAsync();
        var viewer = new ModuleViewerPage(Page);
        await Expect(viewer.Title).ToContainTextAsync(culture == "ar" ? "ع Read this page" : "Read this page");

        // A Manual module completes when the student says so.
        await viewer.Back.ClickAsync();
        await course.ModuleLink(ids["file"]).ClickAsync();
        await Expect(viewer.MarkComplete).ToBeVisibleAsync();
        await viewer.MarkComplete.ClickAsync();
        await Expect(viewer.Done).ToBeVisibleAsync();

        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ProgressText).ToContainTextAsync("2");
        await Expect(course.ModuleState(ids["page"])).ToContainTextAsync(Resx.In(culture, "CompletionState_Completed"));
        await Expect(course.ModuleState(ids["file"])).ToContainTextAsync(Resx.In(culture, "CompletionState_Completed"));
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Continue_opens_next_incomplete_module(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var (world, ids) = await SetUpAsync(async (api, w) => new Dictionary<string, Guid>
        {
            ["one"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "First", rule: "Manual", html: "<p>1</p>"),
            ["two"] = await M4Kit.AddModuleAsync(api, w, 0, "Page", "Second", rule: "Manual", html: "<p>2</p>"),
            ["three"] = await M4Kit.AddModuleAsync(api, w, 1, "Page", "Third", rule: "Manual", html: "<p>3</p>"),
        });

        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var course = new MyCoursePage(Page);
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ContinueTitle).ToContainTextAsync("First");

        // Complete the first module; "continue" moves on to the second, across lessons after that.
        var viewer = new ModuleViewerPage(Page);
        await viewer.OpenAsync(world.ClassCourseId, ids["one"]);
        await viewer.MarkComplete.ClickAsync();
        await Expect(viewer.Done).ToBeVisibleAsync();
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ContinueTitle).ToContainTextAsync("Second");
        await course.ContinueOpen.ClickAsync();
        await Expect(viewer.Title).ToContainTextAsync("Second");

        await viewer.MarkComplete.ClickAsync();
        await Expect(viewer.Done).ToBeVisibleAsync();
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ContinueTitle).ToContainTextAsync("Third");

        await viewer.OpenAsync(world.ClassCourseId, ids["three"]);
        await viewer.MarkComplete.ClickAsync();
        await Expect(viewer.Done).ToBeVisibleAsync();
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ContinueDone).ToBeVisibleAsync();
    }

    /// <summary>Signs in as the Org Admin, builds the world and the modules through the API, then clears the session.</summary>
    private async Task<(M4World World, Dictionary<string, Guid> Modules)> SetUpAsync(Func<ApiSetup, M4World, Task<Dictionary<string, Guid>>> modules)
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var api = new ApiSetup(Page);
        var world = await M4Kit.BuildWorldAsync(api, M4Names.Suffix());
        var ids = await modules(api, world);
        await M4Kit.SignOutAsync(Page);
        return (world, ids);
    }
}
