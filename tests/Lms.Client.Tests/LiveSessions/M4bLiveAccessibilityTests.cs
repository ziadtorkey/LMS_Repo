using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lms.Client.Pages;
using Lms.Client.Pages.LiveSessions;
using Lms.Client.Services.Api;
using Lms.Client.Services.LiveSessions;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.LiveSessions;

/// <summary>
/// M4b hardening (T19) of the live-session authoring pages, in Arabic and English: every control has a name, every field a label,
/// dialogs are named and keyboard reachable, saves and cancellations are announced in a polite live region, and the E2E hooks that
/// QA's page objects use exist.
/// </summary>
public sealed class M4bLiveAccessibilityTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid Lesson = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(8);

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly FakeLiveSessionsApi _live = new(seedDemo: false) { ServerNow = () => Now };

    public M4bLiveAccessibilityTests()
    {
        Services.AddSingleton<ILiveSessionsApi>(_live);
        Services.AddSingleton<TimeProvider>(new FixedTime(Now));
        Services.AddSingleton<IActivityOverviewApi>(new FakeActivityOverviewApi());
        Services.AddSingleton<IModulesApi>(new FakeModulesApi());
    }

    public static IEnumerable<object[]> Cultures => [["en"], ["ar"]];

    private static string Name(IElement e)
    {
        var label = e.GetAttribute("aria-label") ?? e.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(label)) return label;
        var labelledBy = e.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy) && e.Owner?.GetElementById(labelledBy) is { } target && !string.IsNullOrWhiteSpace(target.TextContent)) return target.TextContent;
        return e.TextContent.Trim();
    }

    private static List<string> Audit(string markup)
    {
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument("<html><body>" + markup + "</body></html>");
        var problems = new List<string>();
        foreach (var b in document.QuerySelectorAll("button, a[href], [role=button]"))
            if (string.IsNullOrWhiteSpace(Name(b))) problems.Add($"control without a name: {b.OuterHtml[..Math.Min(140, b.OuterHtml.Length)]}");
        foreach (var i in document.QuerySelectorAll("input:not([type=hidden]), textarea, select"))
        {
            var id = i.GetAttribute("id");
            var labelled = !string.IsNullOrWhiteSpace(i.GetAttribute("aria-label")) || !string.IsNullOrWhiteSpace(i.GetAttribute("aria-labelledby"))
                || (id is not null && document.QuerySelector($"label[for='{id}']") is not null) || i.Closest("label") is not null
                || i.Closest(".mud-input-control")?.QuerySelector("label, .mud-input-label") is not null
                || i.Closest(".mud-radio, .mud-checkbox, .mud-switch") is not null;
            if (!labelled) problems.Add($"field without a label: {i.OuterHtml[..Math.Min(300, i.OuterHtml.Length)]}");
        }
        foreach (var iframe in document.QuerySelectorAll("iframe"))
            if (string.IsNullOrWhiteSpace(iframe.GetAttribute("title"))) problems.Add("iframe without a title");
        foreach (var t in document.QuerySelectorAll("table"))
            if (t.QuerySelectorAll("th").Length == 0) problems.Add("table without header cells");
        return problems;
    }

    private static void AssertClean(string markup, string page)
    {
        var problems = Audit(markup).Distinct().ToList();
        Assert.True(problems.Count == 0, $"{page}: " + string.Join("\n", problems));
    }

    private async Task<IRenderedComponent<LiveSessionsPage>> OpenAsync(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.LiveSessions.View, Permissions.LiveSessions.Manage, Permissions.LessonPlans.View, Permissions.Modules.Manage);
        RenderProviders();
        var page = Render<LiveSessionsPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=live-title]", Wait);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=live-loading]")), Wait);
        return page;
    }

    private LiveSessionDto Seed(string title = "Review", double hours = 3, Guid? series = null, int? index = null, int? count = null) =>
        _live.Seed(Cc, Lesson, Now.AddHours(hours), 60, LiveSessionStatuses.Scheduled, title, series, index, count);

    [Theory, MemberData(nameof(Cultures))]
    public async Task The_live_sessions_page_and_its_dialogs_are_accessible(string culture)
    {
        var series = Guid.NewGuid();
        var first = Seed("Weekly", 30, series, 1, 3);
        Seed("Weekly", 200, series, 2, 3);
        Seed("Sync issue", 5);
        _live.Seed(Cc, Lesson, Now.AddHours(8), 60, LiveSessionStatuses.SyncFailed, "Waiting");
        var page = await OpenAsync(culture);
        AssertClean(page.Markup, "live sessions list");

        page.Find("[data-testid=live-schedule]").Click();
        Dialogs.WaitForElement("[data-testid=live-save]", Wait);
        Dialogs.Find("input[data-testid=repeat-weekly]").Click();
        Dialogs.WaitForElement("[data-testid=repeat-days]", Wait);
        AssertClean(Dialogs.Markup, "schedule dialog (weekly)");
        Assert.NotEmpty(Dialogs.FindAll(".mud-dialog-title, .mud-dialog-title *"));   // the dialog has a title
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task The_scope_and_recording_dialogs_are_accessible(string culture)
    {
        var series = Guid.NewGuid();
        var first = Seed("Weekly", 30, series, 1, 3);
        Seed("Weekly", 200, series, 2, 3);
        var page = await OpenAsync(culture);

        page.Find($"[data-testid=live-edit-{first.Id}]").Click();
        Dialogs.WaitForElement("[data-testid=series-scope]", Wait);
        AssertClean(Dialogs.Markup, "series scope dialog");
        Assert.Equal("radiogroup", Dialogs.Find("[data-testid=series-scope]").GetAttribute("role"));
        Dialogs.Find("[data-testid=scope-back]").Click();

        page.Find($"[data-testid=live-recording-{first.Id}]").Click();
        Dialogs.WaitForElement("input[data-testid=recording-url]", Wait);
        AssertClean(Dialogs.Markup, "recording dialog");
    }

    [Theory, MemberData(nameof(Cultures))]
    public async Task The_home_live_cards_are_accessible(string culture)
    {
        Seed("Office hours", 3);
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.LiveSessions.View);
        RenderProviders();

        var home = Render<Home>();
        home.WaitForElement("[data-testid=home-live-upcoming]", Wait);

        AssertClean(home.Markup, "home live card");
    }

    [Fact]
    public async Task Scheduling_a_session_is_announced_politely()
    {
        var page = await OpenAsync("en");
        page.Find("[data-testid=live-schedule]").Click();
        Dialogs.WaitForElement("[data-testid=live-save]", Wait);
        Dialogs.Find("input[data-testid=module-title-ar]").Change("مراجعة");
        Dialogs.Find("input[data-testid=module-title-en]").Change("Revision");
        var lesson = SelectWithTestId(Dialogs, "live-lesson");
        await ChooseAsync(lesson, (Guid?)Lesson);
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudDatePicker>().Instance.DateChanged.InvokeAsync(Now.Date.AddDays(1)));

        Dialogs.Find("[data-testid=live-save]").Click();

        var region = page.WaitForElement("[data-testid=live-announce]", Wait);
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        page.WaitForAssertion(() => Assert.Contains("scheduled", page.Find("[data-testid=live-announce]").TextContent), Wait);
    }

    [Fact]
    public async Task Cancelling_a_session_is_announced_politely()
    {
        var single = Seed("Review", 3);
        var page = await OpenAsync("en");
        page.Find($"[data-testid=live-cancel-{single.Id}]").Click();
        Dialogs.WaitForElement(".mud-dialog button.mud-button-filled", Wait);

        Dialogs.Find(".mud-dialog button.mud-button-filled").Click();

        page.WaitForAssertion(() => Assert.Contains("cancelled", page.Find("[data-testid=live-announce]").TextContent), Wait);
    }

    [Fact]
    public async Task Saving_a_recording_link_is_announced_politely()
    {
        var s = Seed("Review", 3);
        var page = await OpenAsync("en");
        page.Find($"[data-testid=live-recording-{s.Id}]").Click();
        Dialogs.WaitForElement("input[data-testid=recording-url]", Wait);
        Dialogs.Find("input[data-testid=recording-url]").Input("https://drive.google.com/file/d/abc/view");

        Dialogs.Find("[data-testid=recording-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("Recording link saved", page.Find("[data-testid=live-announce]").TextContent), Wait);
    }

    [Fact]
    public async Task The_hooks_QA_page_objects_rely_on_exist()
    {
        var series = Guid.NewGuid();
        var s = Seed("Weekly", 30, series, 1, 3);
        Seed("Weekly", 200, series, 2, 3);
        var page = await OpenAsync("en");

        foreach (var id in new[] { "live-title", "live-schedule", "live-table", "live-filter-upcoming", "live-filter-past", $"live-row-{s.Id}",
                     $"live-edit-{s.Id}", $"live-recording-{s.Id}", $"live-cancel-{s.Id}", $"live-series-{s.Id}", "live-announce" })
            Assert.True(page.FindAll($"[data-testid={id}]").Count > 0, $"missing data-testid={id}");
        Assert.NotEmpty(page.FindAll("[data-testid=live-table] [data-testid=live-status]"));

        page.Find("[data-testid=live-schedule]").Click();
        Dialogs.WaitForElement("[data-testid=live-save]", Wait);
        // The pickers are typable (Editable) and the test id reaches a real text input, so an E2E can type the date and the time.
        Assert.NotEmpty(Dialogs.FindAll("input[data-testid=live-time], [data-testid=live-time] input"));
        Assert.NotEmpty(Dialogs.FindAll("input[data-testid=live-date], [data-testid=live-date] input"));
        foreach (var id in new[] { "live-zone", "live-duration", "live-lesson", "live-date", "live-time", "repeat-once", "repeat-weekly" })
            Assert.True(Dialogs.FindAll($"[data-testid={id}]").Count > 0, $"missing data-testid={id} in the schedule dialog");
    }
}
