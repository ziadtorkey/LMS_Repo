using System.Globalization;
using Bunit;
using Lms.Client.Components.LiveSessions;
using Lms.Client.Services.Api;
using Lms.Client.Services.LiveSessions;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.LiveSessions;

/// <summary>The live session panel in the module viewer and its join button (M4b Task 15).</summary>
public sealed class LiveSessionPanelTests : StaffTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly DateTime Start = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CourseId = Guid.NewGuid();
    private static readonly Guid LessonKey = Guid.NewGuid();

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Forwards everything to the fake, but holds the join answer until the test lets it go.</summary>
    private sealed class GatedApi(FakeLiveSessionsApi inner) : ILiveSessionsApi
    {
        public TaskCompletionSource<bool> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Gated { get; set; }

        public Task<PagedResult<LiveSessionListItemDto>> ListAsync(Guid id, LiveSessionQuery q, CancellationToken ct = default) => inner.ListAsync(id, q, ct);
        public Task<IReadOnlyList<LiveSessionDto>> CreateAsync(Guid id, CreateLiveSessionRequest r, CancellationToken ct = default) => inner.CreateAsync(id, r, ct);
        /// <summary>Changes what GET returns (the fake validates what it stores, so a bad link or a different window is set here).</summary>
        public Func<LiveSessionDto, LiveSessionDto>? Edit { get; set; }

        public async Task<LiveSessionDto> GetAsync(Guid id, CancellationToken ct = default)
        {
            var session = await inner.GetAsync(id, ct);
            return Edit is null ? session : Edit(session);
        }
        public Task<LiveSessionDto> UpdateAsync(Guid id, UpdateLiveSessionRequest r, CancellationToken ct = default) => inner.UpdateAsync(id, r, ct);
        public Task DeleteAsync(Guid id, string scope = SeriesEditScopes.This, CancellationToken ct = default) => inner.DeleteAsync(id, scope, ct);

        public async Task<LiveSessionJoinDto> JoinAsync(Guid id, CancellationToken ct = default)
        {
            if (Gated) await Gate.Task;
            return await inner.JoinAsync(id, ct);
        }

        public Task<LiveSessionDto> SetRecordingAsync(Guid id, RecordingLinkRequest r, CancellationToken ct = default) => inner.SetRecordingAsync(id, r, ct);
        public Task<PagedResult<LiveParticipantDto>> ListParticipantsAsync(Guid id, LiveParticipantQuery q, CancellationToken ct = default) => inner.ListParticipantsAsync(id, q, ct);
        public Task<ProviderStatusDto> GetProviderStatusAsync(CancellationToken ct = default) => inner.GetProviderStatusAsync(ct);
        public Task<IReadOnlyList<UpcomingLiveSessionDto>> ListUpcomingAsync(CancellationToken ct = default) => inner.ListUpcomingAsync(ct);
    }

    private readonly ManualTime _time = new();
    private readonly FakeLiveSessionsApi _fake = new(seedDemo: false) { AsStudent = true };
    private readonly GatedApi _api;
    private DateTime _serverNow;

    public LiveSessionPanelTests()
    {
        _serverNow = _time.Now.UtcDateTime;
        _fake.ServerNow = () => _serverNow;
        _api = new GatedApi(_fake);
        Services.AddSingleton<ILiveSessionsApi>(_api);
        Services.AddSingleton<TimeProvider>(_time);
        Culture.Current = new CultureInfo("en");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void Advance(TimeSpan by)
    {
        _serverNow += by;
        _time.Now += by;
    }

    private LiveSessionDto Seed(DateTime? startsAt = null, string status = LiveSessionStatuses.Scheduled, int minutes = 60) =>
        _fake.Seed(CourseId, LessonKey, startsAt ?? Start, minutes, status, "Fractions live");

    private async Task<IRenderedComponent<LiveSessionPanel>> OpenAsync(Guid? sessionId, params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.LiveSessions.View, Permissions.Modules.View] : permissions);
        RenderProviders();
        var cut = Render<LiveSessionPanel>(p => p.Add(x => x.SessionId, sessionId).Add(x => x.ClassCourseId, CourseId));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=live-loading]")), Wait);
        return cut;
    }

    private static bool Disabled(IRenderedComponent<LiveSessionPanel> cut) => cut.Find("[data-testid=join-button]").HasAttribute("disabled");

    [Fact]
    public async Task Join_is_disabled_before_the_window_and_enabled_inside_it_by_server_time()
    {
        var session = Seed(Start.AddHours(2).AddMinutes(10)); // starts in 2 h 10 min after 06:00 + 4 h = 10:00 + ... window opens 10 min earlier
        var cut = await OpenAsync(session.Id);

        Assert.True(Disabled(cut));
        Assert.Contains("Starts in", cut.Find("[data-testid=live-countdown]").TextContent);

        Advance(session.JoinOpensAtUtc - _serverNow); // the window opens by the server clock; no browser time involved
        cut.Render();

        Assert.False(Disabled(cut));
        // The one-second tick notices the change and announces it politely (the timer is the component's own, so wait for it).
        cut.WaitForAssertion(() => Assert.Contains("can join now", cut.Find("[data-testid=live-announce]").TextContent), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Countdown_counts_from_the_server_time_not_the_browser_clock()
    {
        var session = Seed(_serverNow.AddHours(2).AddMinutes(10));
        var cut = await OpenAsync(session.Id);

        Assert.Equal("Starts in 2 h 10 min", cut.Find("[data-testid=live-countdown]").TextContent.Trim());

        Advance(TimeSpan.FromMinutes(5));
        cut.Render();

        Assert.Equal("Starts in 2 h 5 min", cut.Find("[data-testid=live-countdown]").TextContent.Trim());
    }

    [Fact]
    public async Task Join_opens_the_tab_synchronously_before_the_api_answers()
    {
        var session = Seed(_serverNow.AddMinutes(5)); // inside the window
        JSInterop.Setup<string?>("lmsLiveJoin.open").SetResult("tab-1");
        _api.Gated = true;
        var cut = await OpenAsync(session.Id);

        cut.Find("[data-testid=join-button]").Click();

        Assert.Single(JSInterop.Invocations["lmsLiveJoin.open"]);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "lmsLiveJoin.navigate");
        _api.Gate.SetResult(true);
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["lmsLiveJoin.navigate"]), Wait);
    }

    [Fact]
    public async Task Join_tab_reference_is_not_null_on_click_and_opener_is_cleared_at_once()
    {
        var session = Seed(_serverNow.AddMinutes(5));
        JSInterop.Setup<string?>("lmsLiveJoin.open").SetResult("tab-1");
        JSInterop.Setup<bool>("lmsLiveJoin.navigate", _ => true).SetResult(true);
        var cut = await OpenAsync(session.Id);

        cut.Find("[data-testid=join-button]").Click();

        // The page keeps the reference the script returned and hands it back to navigate: no fallback link was needed.
        cut.WaitForAssertion(() => Assert.Equal("tab-1", JSInterop.Invocations["lmsLiveJoin.navigate"].Single().Arguments[0]), Wait);
        Assert.Empty(cut.FindAll("[data-testid=join-blocked]"));
        // The script clears the opener before it can run anything, and never passes the noopener feature (which would make window.open return null).
        var script = File.ReadAllText(LiveJoinScript());
        Assert.Contains("window.open('about:blank', '_blank')", script);
        Assert.DoesNotContain("'_blank', 'noopener", script);
        Assert.True(script.IndexOf("tab.opener = null", StringComparison.Ordinal) < script.IndexOf("this.tabs[id] = tab", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Join_sets_the_tab_location_only_after_the_api_answers()
    {
        var session = Seed(_serverNow.AddMinutes(5));
        JSInterop.Setup<string?>("lmsLiveJoin.open").SetResult("tab-1");
        JSInterop.Setup<bool>("lmsLiveJoin.navigate", _ => true).SetResult(true);
        _api.Gated = true;
        var cut = await OpenAsync(session.Id);

        cut.Find("[data-testid=join-button]").Click();
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "lmsLiveJoin.navigate");
        _api.Gate.SetResult(true);

        cut.WaitForAssertion(() => Assert.Equal($"https://meet.example.test/j/{session.Id:N}", JSInterop.Invocations["lmsLiveJoin.navigate"].Single().Arguments[1]), Wait);
    }

    [Fact]
    public async Task Fallback_link_alone_carries_noopener_noreferrer()
    {
        var session = Seed(_serverNow.AddMinutes(5));
        JSInterop.Setup<string?>("lmsLiveJoin.open").SetResult(null); // the browser blocked the tab
        var cut = await OpenAsync(session.Id);

        cut.Find("[data-testid=join-button]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=join-fallback-link]")), Wait);
        var link = cut.Find("[data-testid=join-fallback-link]");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Empty(JSInterop.Invocations["lmsLiveJoin.open"].Single().Arguments); // no feature string such as "noopener" on window.open
    }

    [Fact]
    public async Task Join_url_is_not_in_the_markup_before_the_click()
    {
        var session = Seed(_serverNow.AddMinutes(5));
        var cut = await OpenAsync(session.Id);

        Assert.DoesNotContain("meet.example.test", cut.Markup);
        Assert.DoesNotContain("http", cut.Markup);
    }

    [Fact]
    public async Task Join_failure_closes_the_blank_tab_and_shows_the_error()
    {
        var session = Seed(_serverNow.AddMinutes(5));
        JSInterop.Setup<string?>("lmsLiveJoin.open").SetResult("tab-1");
        var cut = await OpenAsync(session.Id);
        _fake.Failure = new ApiProblemException(503, "provider_unavailable", null, null);

        cut.Find("[data-testid=join-button]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=join-error]")), Wait);
        Assert.Equal("tab-1", JSInterop.Invocations["lmsLiveJoin.close"].Single().Arguments[0]);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "lmsLiveJoin.navigate");
        Assert.NotNull(cut.Find("[data-testid=join-retry]"));
    }

    [Fact]
    public async Task Join_falls_back_to_a_visible_link_when_the_tab_is_blocked()
    {
        var session = Seed(_serverNow.AddMinutes(5));
        JSInterop.Setup<string?>("lmsLiveJoin.open").SetResult(null);
        var cut = await OpenAsync(session.Id);

        cut.Find("[data-testid=join-button]").Click();

        cut.WaitForAssertion(() => Assert.Equal($"https://meet.example.test/j/{session.Id:N}", cut.Find("[data-testid=join-fallback-link]").GetAttribute("href")), Wait);
        Assert.Contains("blocked", cut.Find("[data-testid=join-blocked]").TextContent);
    }

    [Fact]
    public async Task Join_in_a_closed_window_refreshes_the_panel()
    {
        // The page thinks the window is open (the clock moved on), but the server says it is not (409).
        var session = Seed(_serverNow.AddMinutes(30));
        JSInterop.Setup<string?>("lmsLiveJoin.open").SetResult("tab-1");
        var cut = await OpenAsync(session.Id);
        Advance(TimeSpan.FromMinutes(25)); // the page's clock is ahead of the fake server's: pretend the server did not move
        _serverNow -= TimeSpan.FromMinutes(25);
        cut.Render();
        var reads = _fake.Calls.Count(c => c.StartsWith("get", StringComparison.Ordinal));

        cut.Find("[data-testid=join-button]").Click();

        cut.WaitForAssertion(() => Assert.True(_fake.Calls.Count(c => c.StartsWith("get", StringComparison.Ordinal)) > reads), Wait);
        // No toast and no error box: the panel simply shows the state the server now sees.
        Assert.Empty(cut.FindAll("[data-testid=join-error]"));
    }

    [Fact]
    public async Task Invisible_session_module_is_not_found()
    {
        var cut = await OpenAsync(Guid.NewGuid());

        Assert.NotNull(cut.Find("[data-testid=live-not-available]"));
        Assert.Contains("isn't available", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid=live-title]"));
        Assert.Empty(cut.FindAll("[data-testid=join-button]"));
        Assert.NotNull(cut.Find("[data-testid=live-back]"));
    }

    [Fact]
    public async Task Recording_link_has_nofollow_noopener_and_shows_its_host()
    {
        var session = Seed(_serverNow.AddHours(-3), LiveSessionStatuses.Ended);
        _fake.AsStudent = false;
        await _fake.SetRecordingAsync(session.Id, new RecordingLinkRequest("https://zoom.us/rec/share/abc"));
        _fake.AsStudent = true;
        var cut = await OpenAsync(session.Id);

        var link = cut.Find("[data-testid=live-recording]");

        Assert.Equal("https://zoom.us/rec/share/abc", link.GetAttribute("href"));
        Assert.Equal("noopener noreferrer nofollow", link.GetAttribute("rel"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("zoom.us", cut.Find("[data-testid=live-recording-host]").TextContent);
    }

    [Fact]
    public async Task A_recording_link_that_is_not_http_or_https_is_not_rendered_as_a_link()
    {
        var bad = Seed(_serverNow.AddHours(-3), LiveSessionStatuses.Ended);
        _api.Edit = x => x with { RecordingUrl = "javascript:alert(1)" };
        var cut = await OpenAsync(bad.Id);

        Assert.Empty(cut.FindAll("[data-testid=live-recording]"));
        Assert.DoesNotContain("javascript:", cut.Markup);
        Assert.NotNull(cut.Find("[data-testid=live-no-recording]"));
    }

    [Theory]
    [InlineData(10, "en", "10 min")]
    [InlineData(5, "ar", "5 دقائق")]
    [InlineData(15, "ar", "15 دقيقة")]
    [InlineData(2, "ar", "دقيقتين")]
    public async Task Join_window_hint_uses_the_plural_family_of_the_language(int minutes, string culture, string expected)
    {
        Culture.Current = new CultureInfo(culture);
        var start = _serverNow.AddHours(3);
        var session = Seed(start);
        _api.Edit = x => x with { JoinOpensAtUtc = x.StartsAtUtc.AddMinutes(-minutes) };
        var cut = await OpenAsync(session.Id);

        Assert.Contains(expected, cut.Find("[data-testid=live-caption]").TextContent);
    }

    [Fact]
    public async Task Ended_without_recording_says_so()
    {
        var session = Seed(_serverNow.AddHours(-3), LiveSessionStatuses.Ended);
        var cut = await OpenAsync(session.Id);

        Assert.NotNull(cut.Find("[data-testid=live-no-recording]"));
        Assert.Empty(cut.FindAll("[data-testid=live-recording]"));
    }

    [Fact]
    public async Task Cancelled_and_waiting_for_zoom_states()
    {
        var cancelled = Seed(_serverNow.AddHours(1), LiveSessionStatuses.Cancelled);
        var cut = await OpenAsync(cancelled.Id);
        Assert.NotNull(cut.Find("[data-testid=live-cancelled]"));
        Assert.Empty(cut.FindAll("[data-testid=join-button]"));

        var failed = Seed(_serverNow.AddMinutes(5), LiveSessionStatuses.SyncFailed); // inside the window, but Zoom is not ready
        var waiting = Render<LiveSessionPanel>(p => p.Add(x => x.SessionId, failed.Id).Add(x => x.ClassCourseId, CourseId));
        waiting.WaitForAssertion(() => Assert.True(Disabled(waiting)), Wait);
        Assert.Contains("still being set up", waiting.Find("[data-testid=live-caption]").TextContent);
    }

    [Fact]
    public async Task Staff_start_the_meeting_and_open_the_participants_of_an_ended_session()
    {
        var demo = new FakeLiveSessionsApi();
        var ended = (await demo.ListAsync(FakeLiveSessionsApi.DemoClassCourseId, new LiveSessionQuery(Status: LiveSessionStatuses.Ended))).Items[0];
        Services.AddSingleton<ILiveSessionsApi>(demo);
        await GrantAsync(Permissions.LiveSessions.View, Permissions.LiveSessions.Manage, Permissions.Modules.View);
        RenderProviders();
        var cut = Render<LiveSessionPanel>(p => p.Add(x => x.SessionId, ended.Id).Add(x => x.ClassCourseId, FakeLiveSessionsApi.DemoClassCourseId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=live-participants]")), Wait);

        cut.Find("[data-testid=live-participants]").Click();

        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=participants-table]")), Wait);
        var chips = Dialogs.FindAll("[data-testid^=participant-match-]");
        Assert.Contains(chips, c => c.GetAttribute("data-match") == ParticipantMatchStates.Matched);
        Assert.Contains(chips, c => c.GetAttribute("data-match") == ParticipantMatchStates.Guest && c.TextContent.Contains("Guest"));
        Assert.Contains("iPhone (guest)", Dialogs.Markup);
        Assert.NotNull(Dialogs.Find("[data-testid=participants-intro]"));
        Assert.Contains(Dialogs.FindAll("[data-testid=participants-table] td bdi"), b => b.TextContent == "iPhone (guest)");
        Assert.Contains("lms-simple-table", Dialogs.Find("[data-testid=participants-table]").ClassList);
        Assert.NotNull(cut.Find("[data-testid=live-recording-edit]")); // staff can attach or change the recording link
        // Safe at 360 px: the table turns into labelled cards (lms-stack-table, every cell has its data-label).
        Assert.Contains("lms-stack-table", Dialogs.Find("[data-testid=participants-table]").ClassList);
        Assert.All(Dialogs.FindAll("[data-testid=participants-table] tbody td"), td => Assert.False(string.IsNullOrEmpty(td.GetAttribute("data-label"))));
    }

    [Theory]
    [InlineData(LiveSessionStatuses.Ended, "No participants yet")]
    [InlineData(LiveSessionStatuses.Live, "appear here after the session ends")]
    public async Task The_empty_participants_list_says_whether_the_report_is_still_to_come(string status, string expected)
    {
        var empty = new FakeLiveSessionsApi(seedDemo: false);
        var start = status == LiveSessionStatuses.Ended ? _serverNow.AddHours(-3) : _serverNow.AddMinutes(-5);
        var session = empty.Seed(CourseId, LessonKey, start, 60, status, "x");
        Services.AddSingleton<ILiveSessionsApi>(empty);
        await GrantAsync(Permissions.LiveSessions.View, Permissions.LiveSessions.Manage, Permissions.Modules.View);
        RenderProviders();
        var cut = Render<LiveSessionPanel>(p => p.Add(x => x.SessionId, session.Id).Add(x => x.ClassCourseId, CourseId));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=live-participants]")), Wait);

        cut.Find("[data-testid=live-participants]").Click();

        Dialogs.WaitForAssertion(() => Assert.Contains(expected, Dialogs.Find("[data-testid=participants-empty]").TextContent), Wait);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        var session = Seed(_serverNow.AddHours(2).AddMinutes(10));
        var cut = await OpenAsync(session.Id);

        Assert.Equal("تبدأ بعد ساعتين و10 دقائق", cut.Find("[data-testid=live-countdown]").TextContent.Trim());
        Assert.Contains("الانضمام", cut.Find("[data-testid=live-join-zone]").GetAttribute("aria-label"));
        Assert.Contains("10 دقائق", cut.Find("[data-testid=live-caption]").TextContent);
    }

    [Fact]
    public async Task Status_is_shown_with_the_shared_status_chip_and_explains_a_session_waiting_for_zoom()
    {
        var session = Seed(_serverNow.AddHours(1), LiveSessionStatuses.SyncFailed);
        var cut = await OpenAsync(session.Id);

        Assert.Equal(LiveSessionStatuses.SyncFailed, cut.Find("[data-testid=live-status]").GetAttribute("data-status"));
        Assert.NotNull(cut.Find("[data-testid=live-status-hint]"));
    }

    [Theory]
    [InlineData(5, "5 دقائق")]
    [InlineData(60, "60 دقيقة")]
    [InlineData(2, "دقيقتان")]
    public async Task Session_duration_uses_the_right_arabic_plural_family(int minutes, string expected)
    {
        Culture.Current = new CultureInfo("ar");
        var session = _fake.Seed(CourseId, LessonKey, _serverNow.AddHours(2), minutes, LiveSessionStatuses.Scheduled, "x");
        var cut = await OpenAsync(session.Id);

        Assert.Contains(expected, cut.Find("[data-testid=live-meta]").TextContent);
    }

    [Fact]
    public async Task The_announcement_text_is_visually_hidden_with_the_apps_own_class()
    {
        var session = Seed(_serverNow.AddMinutes(30));
        var cut = await OpenAsync(session.Id);

        var announce = cut.Find("[data-testid=live-announce]");

        Assert.Contains("lms-visually-hidden", announce.ClassList);
        Assert.DoesNotContain("visually-hidden", announce.ClassList); // the bare class is not defined anywhere: the text would show on screen
    }

    [Fact]
    public void No_component_uses_the_undefined_visually_hidden_class()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lms.Client"))) dir = dir.Parent;
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src", "Lms.Client"), "*.razor", SearchOption.AllDirectories)
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"class=""[^""]*(?<![\w-])visually-hidden"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string LiveJoinScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Lms.Client", "wwwroot", "js", "live-join.js"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Lms.Client", "wwwroot", "js", "live-join.js");
    }
}
