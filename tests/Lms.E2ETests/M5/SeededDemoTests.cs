using System.Diagnostics;
using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M5;

/// <summary>
/// M5 demo 2 against the dev seed of BE2 (#723, <c>M5DemoSeeder</c>): the demo class "DEMO-M5-KG1" with five students, a published conflict-free week,
/// attendance for the last three weeks, one open lesson (today's, or the latest when today is not a school day), a student over the alert threshold
/// (Khalid: 6 absences, 2 lates) and a pending excuse for Sara's last absence from the seeded guardian. No set-up through the API is needed, so
/// these scenarios do not depend on <see cref="M5Kit.BuildWorldAsync"/>. Accounts: <c>teacher@</c>, <c>counselor@</c>, <c>guardian@altagamo3.test</c>
/// with the seed password (the forced change is handled by <see cref="M5Kit.SignInAsync"/>). SKELETON: skipped until #723 and the pages of #636,
/// #637 and the alerts and review pages are on main. One E2E stack at a time: take <c>e2e.lock</c> before a run.
/// </summary>
public sealed class SeededDemoTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string NeedsSeed = "Needs the M5 demo seed #723 and the attendance pages #636, #637 on main";
    private const string NeedsFollowUp = "Needs the M5 demo seed #723 and the alerts and excuse review pages (Tasks 37, 38, PRs not opened) on main";

    /// <summary>Phone, 360 px: the seeded teacher marks all present, then one absent and one late, and saves, in under 30 seconds, with no sideways scroll.</summary>
    [Theory(Skip = NeedsSeed)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Seeded_teacher_takes_the_open_lesson_at_360px_with_two_exceptions(string culture)
    {
        await StartAsync(culture: culture);
        await Page.SetViewportSizeAsync(360, 800);
        Page.SetDefaultTimeout(45_000);
        await M5Kit.SignInAsync(Page, E2ESettings.SeedUser("teacher"), Settings.Password, culture);

        var today = new AttendanceTodayPage(Page);
        await today.OpenAsync();
        var open = Page.Locator("[data-testid^='session-primary-']").First; // the open lesson's primary action
        await Expect(open).ToBeVisibleAsync();
        await open.ClickAsync();
        var take = new AttendanceTakePage(Page);
        await Expect(take.Rows.First).ToBeVisibleAsync();
        var students = await take.StudentIdsAsync();
        Assert.True(students.Count >= 3, "the seeded roster has five students");

        var clock = Stopwatch.StartNew();
        await take.MarkAllPresent.ClickAsync();
        await take.Status(students[0], "Absent").ClickAsync();
        await take.Status(students[1], "Late").ClickAsync();
        await Expect(take.Count("Absent")).ToContainTextAsync("1");
        await Expect(take.Count("Late")).ToContainTextAsync("1");
        await Expect(take.Count("Present")).ToContainTextAsync((students.Count - 2).ToString());
        await take.Save.ClickAsync();
        await Expect(take.Error).ToHaveCountAsync(0);
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"Taking attendance took {clock.Elapsed.TotalSeconds:0.0} s");
        Assert.True(await take.NoHorizontalScrollAsync(), "The take page scrolls sideways at 360 px");
        Assert.True((await take.Save.BoundingBoxAsync()) is { Height: >= 44 }, "Save is a 44 px target");
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>The counselor sees the seeded student over the threshold in the alerts (Khalid) and the pending excuse in the review queue.</summary>
    [Theory(Skip = NeedsFollowUp)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Seeded_counselor_sees_the_alert_and_the_pending_excuse(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        await M5Kit.SignInAsync(Page, E2ESettings.SeedUser("counselor"), Settings.Password, culture);

        var pages = new AttendanceFollowUpPages(Page);
        await pages.OpenAlertsAsync();
        await Expect(pages.AlertRows.First).ToBeVisibleAsync(); // the formula sentence of the student over the threshold
        await pages.OpenReviewAsync();
        await Expect(pages.ReviewRows.First).ToBeVisibleAsync(); // the guardian's pending excuse for the last absence
    }
}
