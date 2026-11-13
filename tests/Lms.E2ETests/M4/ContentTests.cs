using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// M4a exit demo 1 (plan Task 31, #381): a teacher builds lessons with content modules and a student completes them, in Arabic
/// (RTL) and English. The school, the class course with its published plan, the teacher assignment, the students and their groups
/// are set up through the API (<see cref="M4Kit"/>); only what a scenario tests is driven through the UI.
/// </summary>
public sealed class ContentTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private static readonly byte[] Pdf = "%PDF-1.7\n%probe\n"u8.ToArray();
    private static readonly byte[] Mp4 = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2', 0, 0, 0, 0, (byte)'m', (byte)'p', (byte)'4', (byte)'2'];
    private static readonly byte[] Mp3 = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0x21, 0xFF, 0xFB, 0x90, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Un-skip when the module authoring shell (FE T17) is mounted on the plan page (nothing renders AddModuleMenu yet) and bug #428 is fixed.</summary>
    private const string NeedsAuthoringShell = "Needs the module authoring wiring on the lesson plan page (#432) on main";

    [Theory(Skip = NeedsAuthoringShell)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_adds_file_video_audio_page_and_link_modules_and_reorders_them(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var world = await WorldAsync();
        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);

        var plan = new LessonPlanPage(Page);
        await plan.OpenAsync(world.ClassCourseId);
        var lesson = world.LessonKeys[0];
        await Expect(plan.Lesson(lesson)).ToBeVisibleAsync();

        var page = await plan.AddAsync(lesson, "Page");
        await page.SetTitlesAsync("صفحة ١", "Page one");
        await page.TypeBodyAsync("module-body-en", "Hello students");
        await page.SaveAsync();

        var link = await plan.AddAsync(lesson, "Link");
        await link.SetTitlesAsync("رابط", "A link");
        await link.Url.FillAsync("https://example.org/resource");
        await link.SaveAsync();

        var file = await plan.AddAsync(lesson, "File");
        await file.SetTitlesAsync("ملف", "A file");
        await file.UploadAsync("worksheet.pdf", "application/pdf", Pdf);
        await Expect(file.AssetName).ToContainTextAsync("worksheet.pdf");
        await file.SaveAsync();

        var video = await plan.AddAsync(lesson, "Video");
        await video.SetTitlesAsync("فيديو", "A video");
        await video.UploadAsync("clip.mp4", "video/mp4", Mp4);
        await Expect(video.AssetName).ToContainTextAsync("clip.mp4");
        await video.SaveAsync();

        var audio = await plan.AddAsync(lesson, "Audio");
        await audio.SetTitlesAsync("صوت", "An audio");
        await audio.UploadAsync("note.mp3", "audio/mpeg", Mp3);
        await Expect(audio.AssetName).ToContainTextAsync("note.mp3");
        await audio.SaveAsync();

        await Expect(plan.ModuleTitles(lesson)).ToHaveCountAsync(5);
        Assert.Equal(["Page one", "A link", "A file", "A video", "An audio"], await TitlesInOrderAsync(plan, lesson, culture));

        // Reorder: move the last module (audio) to the top with the up control, then check the order sticks after a reload.
        var modules = await Api().ModulesOfAsync(world.ClassCourseId, lesson);
        var audioId = modules.Single(m => m.TitleEn == "An audio").Id;
        for (var i = 0; i < 4; i++)
        {
            await plan.Up(audioId).ClickAsync();
        }

        await Expect(plan.ModuleTitles(lesson).First).ToContainTextAsync(culture == "ar" ? "صوت" : "An audio");
        await plan.OpenAsync(world.ClassCourseId);
        Assert.Equal(["An audio", "Page one", "A link", "A file", "A video"], await TitlesInOrderAsync(plan, lesson, culture));
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    private async Task<string[]> TitlesInOrderAsync(LessonPlanPage plan, Guid lesson, string culture)
    {
        var titles = await plan.ModuleTitles(lesson).AllInnerTextsAsync();
        var map = new Dictionary<string, string>
        {
            ["صفحة ١"] = "Page one", ["رابط"] = "A link", ["ملف"] = "A file", ["فيديو"] = "A video", ["صوت"] = "An audio",
        };
        return titles.Select(t => culture == "ar" ? map.GetValueOrDefault(t.Trim(), t.Trim()) : t.Trim()).ToArray();
    }

    private ApiProbe Api() => new(Page);

    private async Task<M4World> WorldAsync()
    {
        // Signed in as the seeded Org Admin only long enough to set the world up through the API; the scenario then signs in as itself.
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var world = await M4Kit.BuildWorldAsync(new ApiSetup(Page), M4Names.Suffix());
        await M4Kit.SignOutAsync(Page);
        return world;
    }
}

/// <summary>Reads the modules of a lesson through the API as the signed-in session.</summary>
internal sealed class ApiProbe(Microsoft.Playwright.IPage page)
{
    public sealed record Module(Guid Id, string TitleEn);

    public async Task<IReadOnlyList<Module>> ModulesOfAsync(Guid classCourseId, Guid lessonKey)
    {
        var json = await new ApiSetup(page).GetAsync($"/api/class-courses/{classCourseId}/lessons/{lessonKey}/modules");
        var items = json.ValueKind == System.Text.Json.JsonValueKind.Array ? json : json.GetProperty("items");
        return items.EnumerateArray().Select(m => new Module(m.GetProperty("id").GetGuid(), m.GetProperty("titleEn").GetString()!)).ToList();
    }
}

internal static class M4Names
{
    public static string Suffix() => DateTime.UtcNow.ToString("HHmmss") + Guid.NewGuid().ToString("N")[..3];
}
