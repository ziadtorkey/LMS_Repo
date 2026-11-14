using System.Globalization;
using Bunit;
using Lms.Client.Pages;
using Lms.Client.Pages.Content;
using Lms.Client.Services.Api;
using Lms.Client.Services.LiveSessions;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.LiveSessions;

/// <summary>Live sessions on Home and the course home (M4b Task 18, live half; design spec §3.3).</summary>
public sealed class LiveSessionCardsTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid OtherCc = Guid.Parse("00000000-0000-0000-0000-00000000cc02");
    private static readonly Guid Lesson = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    // Midday UTC, so "today" and "in 3 h" do not depend on when the suite runs.
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(8);

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly FakeLiveSessionsApi _live = new(seedDemo: false) { ServerNow = () => Now };

    public LiveSessionCardsTests()
    {
        Services.AddSingleton<ILiveSessionsApi>(_live);
        Services.AddSingleton<IActivityOverviewApi>(new FakeActivityOverviewApi());
        Services.AddSingleton<IModulesApi>(new FakeModulesApi { AsStudent = true, CanEdit = false });
        Services.AddSingleton<TimeProvider>(new FixedTime(Now));
        Culture.Current = new CultureInfo("en");
    }

    private LiveSessionDto Seed(string title, double hoursFromNow, string status = LiveSessionStatuses.Scheduled, int minutes = 60, Guid? cc = null) =>
        _live.Seed(cc ?? Cc, Lesson, Now.AddHours(hoursFromNow), minutes, status, title);

    private async Task<IRenderedComponent<Home>> OpenHomeAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<Home>();
        page.WaitForElement("p, .mud-typography-body1", Wait);
        return page;
    }

    [Fact]
    public async Task Home_cards_depend_on_permissions()
    {
        Seed("Office hours", 3);

        var student = await OpenHomeAsync(Permissions.LiveSessions.View);

        var card = student.WaitForElement("[data-testid=home-live-upcoming]", Wait);
        Assert.Empty(student.FindAll("[data-testid=home-live-today]"));
        Assert.Contains("Upcoming live sessions", card.TextContent);
    }

    [Fact]
    public async Task Without_LiveSessions_View_there_is_no_live_card()
    {
        Seed("Office hours", 3);

        var home = await OpenHomeAsync(Permissions.LessonPlans.View);

        Assert.Empty(home.FindAll("[data-testid^=home-live-]"));
    }

    [Fact]
    public async Task Teacher_sees_todays_sessions_with_manage()
    {
        Seed("Today review", 3);
        Seed("Later this week", 48);
        var home = await OpenHomeAsync(Permissions.LiveSessions.View, Permissions.LiveSessions.Manage);

        var card = home.WaitForElement("[data-testid=home-live-today]", Wait);

        Assert.Contains("Sessions today", card.TextContent);
        Assert.Contains("Today review", card.TextContent);
        Assert.DoesNotContain("Later this week", card.TextContent);
        Assert.Empty(home.FindAll("[data-testid=home-live-upcoming]"));
    }

    [Fact]
    public async Task Empty_cards_say_so_in_plain_words()
    {
        var student = await OpenHomeAsync(Permissions.LiveSessions.View);

        Assert.Contains("No live sessions in the coming days.", student.WaitForElement("[data-testid=home-live-upcoming]", Wait).TextContent);
    }

    [Fact]
    public async Task Rows_link_to_the_module_and_never_carry_a_join_url()
    {
        var s = Seed("Office hours", 3);
        var home = await OpenHomeAsync(Permissions.LiveSessions.View);

        var row = home.WaitForElement($"[data-testid=home-live-row-{s.Id}]", Wait);

        Assert.Equal($"class-courses/{Cc}/modules/{s.Module.Id}", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("meet.example", home.Markup);
        Assert.Contains("in 3 h", row.TextContent);   // a Scheduled session in the next 24 h shows its short countdown
    }

    [Fact]
    public async Task The_full_date_is_available_to_screen_readers_although_the_date_block_is_hidden()
    {
        var s = Seed("Office hours", 3);
        var home = await OpenHomeAsync(Permissions.LiveSessions.View);

        var row = home.WaitForElement($"[data-testid=home-live-row-{s.Id}]", Wait);

        Assert.Equal("true", row.QuerySelector(".lms-date-block")!.GetAttribute("aria-hidden"));
        var date = row.QuerySelector("[data-testid=home-live-date]")!;
        Assert.Contains("lms-visually-hidden", date.ClassName);
        Assert.Equal(s.StartsAtUtc.ToString("yyyy-MM-dd"), date.TextContent.Trim());
        Assert.Contains(s.StartsAtUtc.ToString("HH:mm"), row.QuerySelector(".lms-item-caption")!.TextContent);
    }

    [Fact]
    public async Task A_session_in_its_join_window_shows_the_live_chip_and_a_join_link()
    {
        var s = Seed("Workshop", -0.25, LiveSessionStatuses.Live);
        var home = await OpenHomeAsync(Permissions.LiveSessions.View);

        var row = home.WaitForElement($"[data-testid=home-live-row-{s.Id}]", Wait);

        Assert.Equal("Live", row.QuerySelector("[data-testid=live-status]")!.GetAttribute("data-status"));
        var join = row.QuerySelector("[data-testid=home-live-join]")!;
        Assert.Equal($"class-courses/{Cc}/modules/{s.Module.Id}", join.GetAttribute("href"));
    }

    [Fact]
    public async Task A_session_before_the_join_window_has_no_join_link()
    {
        var s = Seed("Tomorrow", 30);
        var home = await OpenHomeAsync(Permissions.LiveSessions.View);

        var row = home.WaitForElement($"[data-testid=home-live-row-{s.Id}]", Wait);

        Assert.Empty(row.QuerySelectorAll("[data-testid=home-live-join]"));
    }

    [Fact]
    public async Task Course_home_lists_upcoming_sessions_in_server_order()
    {
        Seed("Third", 30);
        Seed("First", 2);
        Seed("Second", 5);
        Seed("Other course", 1, cc: OtherCc);
        await GrantAsync(Permissions.Modules.View, Permissions.LessonPlans.View, Permissions.LiveSessions.View);
        Plans.HideHiddenItems = true;
        RenderProviders();

        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));
        var card = page.WaitForElement("[data-testid=home-live-upcoming]", Wait);

        var titles = card.QuerySelectorAll("[data-testid^=home-live-row-] a").Select(a => a.TextContent.Trim()).ToList();
        Assert.Equal(["First", "Second", "Third"], titles);
    }

    [Fact]
    public async Task Course_home_links_recent_recordings()
    {
        var ended = Seed("Chapter 3 review", -48, LiveSessionStatuses.Ended);
        await _live.SetRecordingAsync(ended.Id, new RecordingLinkRequest("https://recordings.example.test/c3"));
        Seed("Ended without recording", -50, LiveSessionStatuses.Ended);
        await GrantAsync(Permissions.Modules.View, Permissions.LessonPlans.View, Permissions.LiveSessions.View);
        Plans.HideHiddenItems = true;
        RenderProviders();

        var page = Render<MyCoursePage>(p => p.Add(x => x.Id, Cc));
        var card = page.WaitForElement("[data-testid=course-recordings]", Wait);

        var link = Assert.Single(card.QuerySelectorAll("a"));
        Assert.Equal($"class-courses/{Cc}/modules/{ended.Module.Id}", link.GetAttribute("href"));
        Assert.Contains("Chapter 3 review", link.TextContent);
    }

    [Fact]
    public async Task Cards_render_in_arabic_with_localized_text()
    {
        Culture.Current = new CultureInfo("ar");
        Seed("Office hours", 3);

        var home = await OpenHomeAsync(Permissions.LiveSessions.View);

        var card = home.WaitForElement("[data-testid=home-live-upcoming]", Wait);
        Assert.Contains("الجلسات المباشرة القادمة", card.TextContent);
        Assert.Contains("بعد 3 ساعات", card.TextContent);
    }
}
