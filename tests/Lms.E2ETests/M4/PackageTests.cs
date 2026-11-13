using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// M4b exit demo 2 (plan Task 23, #492): a teacher uploads a SCORM package, previews and attaches it; a student completes it and the
/// progress and the grade event follow; SCORM works on the second page of the SCO; resume after a reload; a rejected package shows its
/// reason; an H5P question set records a score; a preview creates no student data. In Arabic (RTL) and English. SKELETON: every scenario
/// is skipped until the pieces it needs are on main; the page objects (<see cref="PackageUploadDialog"/>, <see cref="PackagePlayerPage"/>)
/// compile against the merged #501 / #552 / #555 test ids. The packages come from the ADR corpus (<see cref="PackageFiles"/>), the content
/// host runs on <c>127.0.0.1:&lt;port&gt;</c> next to the app on <c>localhost</c> (a different site) in the E2E fixture. The demo SCORM
/// package has at least two HTML pages, so the injection of the SCORM API into every page is exercised. One E2E stack at a time: take
/// <c>e2e.lock</c> before a run.
/// </summary>
public sealed class PackageTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    // One reason per dependency, so un-skipping is a search and replace as the tasks land (plan Task 23 "Depends on").
    private const string NeedsUpload = "Needs package upload, validation and attach: API T8a (#523), T8b, UI T16 (#501) on main and the corpus files";
    private const string NeedsScorm = "Needs the content host (T9a/T9b), the SCORM runtime (T10), the player (#552, T17) and the E2E fixture's content host on a second origin";
    private const string NeedsH5p = "Needs the H5P content (T11) next to the SCORM pieces, and its licensed H5P file in the corpus";

    /// <summary>The teacher uploads a SCORM zip, sees it checked, previews it (nothing recorded) and attaches it to a lesson.</summary>
    [Theory(Skip = NeedsUpload)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_uploads_scorm_sees_it_validated_previews_and_attaches_it(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var world = await WorldAsync();
        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);

        var plan = new LessonPlanPage(Page);
        await plan.OpenAsync(world.ClassCourseId);
        var lesson = world.LessonKeys[0];
        await plan.AddMenu(lesson).ClickAsync();
        await Page.GetByTestId("module-add-Scorm").ClickAsync();

        var upload = new PackageUploadDialog(Page);
        await Expect(upload.Dropzone).ToBeVisibleAsync();
        await upload.UploadAsync("scorm12_valid.zip", PackageFiles.Read("scorm12_valid.zip"));
        await Expect(upload.Summary).ToBeVisibleAsync(new() { Timeout = 60_000 }); // checking, then Ready
        await Expect(upload.FoundType).ToContainTextAsync("SCORM");
        await upload.AttachAsync();

        // The module is in the lesson; preview shows the banner and records nothing.
        var modules = await plan.ModuleTitles(lesson).AllInnerTextsAsync();
        Assert.NotEmpty(modules);
        var moduleId = await FirstPackageModuleIdAsync(world);
        var player = new PackagePlayerPage(Page);
        await player.OpenAsync(world.ClassCourseId, moduleId);
        await Expect(player.Preview).ToBeVisibleAsync();
        await player.Preview.ClickAsync();
        await Expect(player.PreviewBanner).ToBeVisibleAsync();
        await Expect(player.FrameElement).ToHaveAttributeAsync("sandbox", "allow-scripts"); // never allow-same-origin

        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>A student runs the SCORM package to completion: the module is Done, the progress moves and a grade event is recorded.</summary>
    [Theory(Skip = NeedsScorm)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_completes_scorm_module_and_progress_and_grade_event_update(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, moduleId) = await SetUpPackageAsync("scorm12_valid.zip", "Scorm");

        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var player = new PackagePlayerPage(Page);
        await player.OpenAsync(world.ClassCourseId, moduleId);
        await Expect(player.ThirdPartyNote).ToBeVisibleAsync();
        await player.StartAsync();
        await Expect(player.FrameElement).ToHaveAttributeAsync("sandbox", "allow-scripts");

        // The package finishes itself (the corpus SCO sets status passed and a score, then LMSFinish): content is observed through the frame.
        await Player_FinishesAsync(player);
        await Expect(player.ResultCard).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(player.ResultScore).ToBeVisibleAsync();

        var course = new MyCoursePage(Page);
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ModuleState(moduleId)).ToContainTextAsync(Resx.In(culture, "CompletionState_Done"));
        var events = await new E2EApi(Page, Settings.BaseUrl).SendAsync("GET", $"/api/class-courses/{world.ClassCourseId}/progress");
        Assert.Equal(200, events.Status);
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>The SCO navigates from page 1 to page 2 and commits on both: the API is injected into every HTML page.</summary>
    [Theory(Skip = NeedsScorm)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Scorm_api_works_on_the_second_page_of_the_sco(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, moduleId) = await SetUpPackageAsync("scorm_multipage_sco.zip", "Scorm");

        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var player = new PackagePlayerPage(Page);
        await player.OpenAsync(world.ClassCourseId, moduleId);
        await player.StartAsync();
        await player.Frame.GetByTestId("sco-next").ClickAsync(); // page 1 -> page 2 inside the frame (the corpus SCO's own test ids)
        await Expect(player.Frame.GetByTestId("sco-page-2")).ToBeVisibleAsync();
        await Expect(player.Frame.GetByTestId("sco-api-status")).ToContainTextAsync("API found"); // the shim answered on page 2 too
        await Expect(player.SaveState).ToContainTextAsync(Resx.In(culture, "Package_Saved"));
    }

    /// <summary>The student reloads in the middle of the SCO: Resume continues from the suspend data.</summary>
    [Theory(Skip = NeedsScorm)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_resumes_scorm_after_reload_with_suspend_data(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, moduleId) = await SetUpPackageAsync("scorm_multipage_sco.zip", "Scorm");

        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var player = new PackagePlayerPage(Page);
        await player.OpenAsync(world.ClassCourseId, moduleId);
        await player.StartAsync();
        await player.Frame.GetByTestId("sco-next").ClickAsync();
        await Expect(player.SaveState).ToContainTextAsync(Resx.In(culture, "Package_Saved"));

        await Page.ReloadAsync(); // a full reload: the attempt is open, the start card offers Resume (same test id, other label)
        await Expect(player.Start).ToContainTextAsync(Resx.In(culture, "Package_Resume"));
        await player.StartAsync();
        await Expect(player.Frame.GetByTestId("sco-page-2")).ToBeVisibleAsync(); // resumed from suspend_data, not from the start
    }

    /// <summary>A hostile package (a frameset) is rejected with the plain reason in the user's language, and nothing is attached.</summary>
    [Theory(Skip = NeedsUpload)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Rejected_package_shows_the_localized_reason(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await WorldAsync();
        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);

        var plan = new LessonPlanPage(Page);
        await plan.OpenAsync(world.ClassCourseId);
        await plan.AddMenu(world.LessonKeys[0]).ClickAsync();
        await Page.GetByTestId("module-add-Scorm").ClickAsync();
        var upload = new PackageUploadDialog(Page);
        await upload.UploadAsync("launch_frameset.zip", PackageFiles.Read("launch_frameset.zip"));
        await Expect(upload.Reject).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(upload.RejectReason).ToContainTextAsync(Resx.In(culture, "Error_package_reason_needs_nested_frames"));
        await Expect(upload.Attach).ToHaveCountAsync(0);
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>The student completes an H5P question set: the score is recorded and shown by the server's answer.</summary>
    [Theory(Skip = NeedsH5p)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_completes_h5p_question_set_and_score_is_recorded(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, moduleId) = await SetUpPackageAsync("h5p_question_set.h5p", "H5p");

        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var player = new PackagePlayerPage(Page);
        await player.OpenAsync(world.ClassCourseId, moduleId);
        await player.StartAsync();
        await player.Frame.GetByRole(Microsoft.Playwright.AriaRole.Radio).First.CheckAsync(); // the question set's own controls
        await player.Frame.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Finish" }).ClickAsync();
        await Expect(player.ResultCard).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(player.ResultScore).ToBeVisibleAsync(); // the score the server judged, never computed in the browser
    }

    /// <summary>A teacher's preview records nothing: no attempt, no completion, no grade event, and the student's module is untouched.</summary>
    [Theory(Skip = NeedsScorm)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Preview_creates_no_student_data(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, moduleId) = await SetUpPackageAsync("scorm12_valid.zip", "Scorm");

        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        var player = new PackagePlayerPage(Page);
        await player.OpenAsync(world.ClassCourseId, moduleId);
        await player.Preview.ClickAsync();
        await Expect(player.PreviewBanner).ToBeVisibleAsync();
        await Expect(player.SaveState).ToHaveCountAsync(0); // the save state is hidden in preview
        await Player_FinishesAsync(player);

        // The student still sees "not started": the preview wrote nothing.
        await M4Kit.SignOutAsync(Page);
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var course = new MyCoursePage(Page);
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ModuleState(moduleId)).ToContainTextAsync(Resx.In(culture, "CompletionState_NotStarted"));
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    // ---------------------------------------------------------------------------------------------------- set-up through the API

    private async Task<M4World> WorldAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var api = new ApiSetup(Page);
        var world = await M4Kit.BuildWorldAsync(api, M4Names.Suffix());
        await EnableScormAsync(api); // Features.ScormContent is off by default
        await M4Kit.SignOutAsync(Page);
        return world;
    }

    /// <summary>Signs in as the org admin, builds the world, uploads a corpus package through the API, waits for Ready and attaches it.</summary>
    private async Task<(M4World World, Guid ModuleId)> SetUpPackageAsync(string corpusFile, string kind)
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var setup = new ApiSetup(Page);
        var world = await M4Kit.BuildWorldAsync(setup, M4Names.Suffix());
        await EnableScormAsync(setup);

        var api = new E2EApi(Page, Settings.BaseUrl);
        var response = await api.SendFormAsync(
            "POST", $"/api/class-courses/{world.ClassCourseId}/packages",
            new Dictionary<string, string> { ["lessonKey"] = world.LessonKeys[0].ToString(), ["kind"] = kind }, "file",
            new Microsoft.Playwright.FilePayload { Name = corpusFile, MimeType = "application/zip", Buffer = PackageFiles.Read(corpusFile) });
        var upload = await api.JsonAsync(response);
        var packageId = upload.GetProperty("id").GetGuid();
        await WaitReadyAsync(setup, packageId);
        var module = await setup.PostAsync($"/api/packages/{packageId}/module", new
        {
            module = new
            {
                titleAr = "حزمة", titleEn = "Package", visibility = "Published", visibleFromUtc = (DateTime?)null, visibleToUtc = (DateTime?)null,
                target = "ClassCourse", groupId = (Guid?)null, completionRule = "Submit", completionMinScore = (decimal?)null,
            },
        });
        var moduleId = module.TryGetProperty("node", out var node) ? node.GetProperty("id").GetGuid() : module.GetProperty("id").GetGuid();
        await M4Kit.SignOutAsync(Page);
        return (world, moduleId);
    }

    private static async Task WaitReadyAsync(ApiSetup api, Guid packageId)
    {
        for (var i = 0; i < 60; i++)
        {
            var status = (await api.GetAsync($"/api/packages/{packageId}")).GetProperty("status").GetString();
            if (status == "Ready")
            {
                return;
            }

            Assert.NotEqual("Rejected", status);
            await Task.Delay(1000);
        }

        Assert.Fail("The package did not reach Ready in 60 s.");
    }

    /// <summary>Turns <c>Features.ScormContent</c> on for the school (the units below inherit it).</summary>
    private static async Task EnableScormAsync(ApiSetup api)
    {
        var school = await api.UnitIdByCodeAsync("S");
        await api.PutAsync($"/api/org-units/{school}/settings/Features.ScormContent", new { valueJson = "true", isLocked = false });
    }

    /// <summary>Lets the corpus SCO finish itself inside the frame (it reports passed and a score, then finishes); a no-op placeholder until the corpus SCO's controls are fixed.</summary>
    private static Task Player_FinishesAsync(PackagePlayerPage player) =>
        player.Frame.GetByTestId("sco-finish").ClickAsync(); // the corpus SCO's own finish control (its test id comes with the corpus, #493)

    /// <summary>The id of the package module just attached to the first lesson (read through the API).</summary>
    private async Task<Guid> FirstPackageModuleIdAsync(M4World world)
    {
        var api = new ApiSetup(Page);
        var modules = await api.GetAsync($"/api/class-courses/{world.ClassCourseId}/lessons/{world.LessonKeys[0]}/modules");
        var list = modules.ValueKind == System.Text.Json.JsonValueKind.Array ? modules : modules.GetProperty("items");
        return list.EnumerateArray().Last().GetProperty("id").GetGuid();
    }
}

/// <summary>The corpus package files of the ADR (BE3's crafted and real packages, <c>tests/Lms.IntegrationTests/Packages/Corpus/files</c>).</summary>
internal static class PackageFiles
{
    public static byte[] Read(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "Lms.IntegrationTests", "Packages", "Corpus", "files")))
        {
            dir = dir.Parent;
        }

        var path = dir is null ? null : Path.Combine(dir.FullName, "tests", "Lms.IntegrationTests", "Packages", "Corpus", "files", name);
        return path is not null && File.Exists(path)
            ? File.ReadAllBytes(path)
            : throw new FileNotFoundException($"Corpus file {name} not found (the corpus lands with the BE3 corpus PR #493).");
    }
}
