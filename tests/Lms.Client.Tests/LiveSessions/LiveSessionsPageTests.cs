using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages.LiveSessions;
using Lms.Client.Services.Api;
using Lms.Client.Services.LiveSessions;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.LiveSessions;

/// <summary>Authoring of live sessions on the class-course page (M4b Task 14).</summary>
public sealed class LiveSessionsPageTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid Lesson = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    // A Monday morning, UTC.
    private static readonly DateTime Now = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly FakeLiveSessionsApi _live = new(seedDemo: false) { ServerNow = () => Now };

    public LiveSessionsPageTests()
    {
        Services.AddSingleton<ILiveSessionsApi>(_live);
        Services.AddSingleton<TimeProvider>(new FixedTime(Now));
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<LiveSessionsPage>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.LiveSessions.View, Permissions.LiveSessions.Manage, Permissions.LessonPlans.View, Permissions.Modules.Manage] : permissions);
        RenderProviders();
        var page = Render<LiveSessionsPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=live-title]", Wait);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=live-loading]")), Wait);
        return page;
    }

    private LiveSessionDto Seed(string status = LiveSessionStatuses.Scheduled, string title = "Weekly review", int daysFromNow = 2, Guid? series = null, int? index = null, int? count = null) =>
        _live.Seed(Cc, Lesson, Now.AddDays(daysFromNow), 60, status, title, series, index, count);

    private async Task OpenDialogAsync(IRenderedComponent<LiveSessionsPage> page)
    {
        page.WaitForElement("[data-testid=live-schedule]", Wait).Click();
        Dialogs.WaitForElement("[data-testid=live-save]", Wait);
        await Task.CompletedTask;
    }

    /// <summary>Fills the title, the lesson and the date of a new session (the start time defaults to 10:00).</summary>
    private async Task FillBasicsAsync(string title = "Revision")
    {
        Dialogs.Find("input[data-testid=live-title-ar]").Change(title + " ar");
        Dialogs.Find("input[data-testid=live-title-en]").Change(title);
        await ChooseAsync(SelectWithTestId(Dialogs, "live-lesson"), (Guid?)Lesson);
        var date = Dialogs.FindComponent<MudDatePicker>();
        await Dialogs.InvokeAsync(() => date.Instance.DateChanged.InvokeAsync(Now.Date.AddDays(1))); // Tuesday
    }

    private AngleSharp.Dom.IElement SaveButton => Dialogs.Find("[data-testid=live-save]");

    private async Task ChooseWeeklyEndingAfterAsync(int count)
    {
        Dialogs.Find("input[data-testid=repeat-weekly]").Click();
        Dialogs.WaitForElement("[data-testid=repeat-days]", Wait);
        Dialogs.Find("input[data-testid=ends-after]").Click();
        var field = Dialogs.FindComponent<MudNumericField<int?>>();
        await Dialogs.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync(count));
    }

    [Fact]
    public async Task Teacher_schedules_single_session_under_own_lesson()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page);
        await FillBasicsAsync();

        SaveButton.Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid^=live-row-]")), Wait);
        var created = (await _live.ListAsync(Cc, new LiveSessionQuery())).Items.Single();
        var full = await _live.GetAsync(created.Id);
        Assert.Equal(Lesson, full.LessonKey);
        Assert.Equal(new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc), full.StartsAtUtc);
        Assert.Equal(60, full.DurationMinutes);
        Assert.Null(full.SeriesId);
        Assert.Equal("Revision", full.Module.TitleEn);
    }

    [Fact]
    public async Task Staff_get_a_participants_action_on_live_and_ended_sessions_that_opens_the_dialog()
    {
        var live = Seed(LiveSessionStatuses.Live, "Live one", daysFromNow: 0);
        var ended = Seed(LiveSessionStatuses.Ended, "Ended one", daysFromNow: 0); // still inside the upcoming list (it started less than 8 hours ago)
        var scheduled = Seed(LiveSessionStatuses.Scheduled, "Later one", daysFromNow: 3);
        var page = await OpenAsync();
        page.WaitForElement($"[data-testid=live-row-{scheduled.Id}]", Wait);

        Assert.Empty(page.FindAll($"[data-testid=live-participants-{scheduled.Id}]"));
        Assert.NotNull(page.Find($"[data-testid=live-participants-{live.Id}]"));
        page.Find($"[data-testid=live-participants-{ended.Id}]").Click();

        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=participants-dialog]")), Wait);
    }

    [Fact]
    public async Task Recurring_requires_end_date_or_count_and_previews_dates()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page);
        await FillBasicsAsync();

        Dialogs.Find("input[data-testid=repeat-weekly]").Click();
        Dialogs.WaitForElement("[data-testid=repeat-days]", Wait);

        // No end yet: the rule cannot be saved and the field says why.
        Assert.True(SaveButton.HasAttribute("disabled"));
        Assert.Contains("Choose an end date", Dialogs.Markup);

        await ChooseWeeklyEndingAfterAsync(4);

        Dialogs.WaitForAssertion(() => Assert.Contains("4 sessions", Dialogs.Find("[data-testid=recurrence-count]").TextContent), Wait);
        var dates = Dialogs.FindAll("[data-testid=recurrence-dates] li").Select(x => x.TextContent.Trim()).ToList();
        Assert.Equal(["2026-11-17 10:00", "2026-11-24 10:00", "2026-12-01 10:00", "2026-12-08 10:00"], dates);
        Assert.False(SaveButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Recurring_is_limited_to_52_occurrences()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page);
        await FillBasicsAsync();

        await ChooseWeeklyEndingAfterAsync(60);

        Dialogs.WaitForElement("[data-testid=recurrence-too-many]", Wait);
        Assert.Contains("at most 52", Dialogs.Find("[data-testid=recurrence-too-many]").TextContent);
        Assert.True(SaveButton.HasAttribute("disabled"));

        var field = Dialogs.FindComponent<MudNumericField<int?>>();
        await Dialogs.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync(52));

        Dialogs.WaitForAssertion(() => Assert.Contains("52 sessions", Dialogs.Find("[data-testid=recurrence-count]").TextContent), Wait);
        Assert.Contains("and 46 more", Dialogs.Find("[data-testid=recurrence-more]").TextContent);
        Assert.False(SaveButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Series_edit_asks_for_the_scope()
    {
        var series = Guid.NewGuid();
        var first = Seed(series: series, index: 1, count: 3, daysFromNow: 2);
        Seed(series: series, index: 2, count: 3, daysFromNow: 9);
        Seed(series: series, index: 3, count: 3, daysFromNow: 16);
        var page = await OpenAsync();

        page.Find($"[data-testid=live-edit-{first.Id}]").Click();
        Dialogs.WaitForElement("[data-testid=series-scope]", Wait);
        Assert.Contains("2 later sessions", Dialogs.Find("[data-testid=scope-following]").TextContent);
        Dialogs.Find("[data-testid=scope-following] input").Change(true);
        Dialogs.Find("[data-testid=scope-continue]").Click();
        Dialogs.WaitForElement("[data-testid=live-save]", Wait);
        Dialogs.Find("input[data-testid=live-title-en]").Change("New title");
        SaveButton.Click();

        page.WaitForAssertion(() => Assert.Contains($"update {first.Id} {SeriesEditScopes.ThisAndFollowing}", _live.Calls), Wait);
    }

    [Fact]
    public async Task A_single_session_edit_does_not_ask_for_a_scope()
    {
        var single = Seed();
        var page = await OpenAsync();

        page.Find($"[data-testid=live-edit-{single.Id}]").Click();

        Dialogs.WaitForElement("[data-testid=live-save]", Wait);
        Assert.Empty(Dialogs.FindAll("[data-testid=series-scope]"));
    }

    [Fact]
    public async Task Cancelling_a_series_asks_for_the_scope_and_sends_it()
    {
        var series = Guid.NewGuid();
        var first = Seed(series: series, index: 1, count: 2, daysFromNow: 2);
        Seed(series: series, index: 2, count: 2, daysFromNow: 9);
        var page = await OpenAsync();

        page.Find($"[data-testid=live-cancel-{first.Id}]").Click();
        Dialogs.WaitForElement("[data-testid=series-scope]", Wait);
        Dialogs.Find("[data-testid=scope-following] input").Change(true);
        Dialogs.Find("[data-testid=scope-continue]").Click();

        page.WaitForAssertion(() => Assert.Contains($"delete {first.Id} {SeriesEditScopes.ThisAndFollowing}", _live.Calls), Wait);
    }

    private static ApiProblemException Busy(int seconds = 300) =>
        new(409, ProblemCodes.provider_unavailable, null, null, retryAfterSeconds: seconds);

    [Fact]
    public async Task Cancelling_a_session_keeps_the_retry_after_when_the_provider_is_busy()
    {
        var single = Seed();
        var page = await OpenAsync();
        _live.Failure = null;
        page.Find($"[data-testid=live-cancel-{single.Id}]").Click();
        Dialogs.WaitForElement(".mud-dialog button.mud-button-filled", Wait);
        _live.Failure = Busy();

        Dialogs.Find(".mud-dialog button.mud-button-filled").Click();

        var error = page.WaitForElement("[data-testid=live-error]", Wait);
        Assert.Contains("Try again in about 5 min", error.TextContent);
    }

    [Fact]
    public async Task Saving_a_recording_link_keeps_the_retry_after_when_the_provider_is_busy()
    {
        var session = Seed();
        var page = await OpenAsync();
        page.Find($"[data-testid=live-recording-{session.Id}]").Click();
        Dialogs.WaitForElement("input[data-testid=recording-url]", Wait);
        Dialogs.Find("input[data-testid=recording-url]").Input("https://drive.google.com/file/d/abc/view");
        _live.Failure = Busy();

        Dialogs.Find("[data-testid=recording-save]").Click();

        var error = Dialogs.WaitForElement("[data-testid=recording-error]", Wait);
        Assert.Contains("Try again in about 5 min", error.TextContent);
    }

    [Fact]
    public async Task SyncFailed_session_shows_the_explanation()
    {
        var failed = Seed(LiveSessionStatuses.SyncFailed, "Exam preparation");
        var page = await OpenAsync();

        var row = page.Find($"[data-testid=live-row-{failed.Id}]");

        Assert.Contains("Waiting for Zoom", row.TextContent);
        Assert.Contains("retry automatically", row.QuerySelector("[data-testid=live-status-hint]")!.TextContent);
    }

    [Fact]
    public async Task Recording_link_rejects_javascript_scheme()
    {
        var session = Seed();
        var page = await OpenAsync();
        page.Find($"[data-testid=live-recording-{session.Id}]").Click();
        Dialogs.WaitForElement("[data-testid=recording-url]", Wait);

        Dialogs.Find("input[data-testid=recording-url]").Input("javascript:alert(1)");

        Assert.Contains("Use a link that starts with https://", Dialogs.Markup);
        Assert.True(Dialogs.Find("[data-testid=recording-save]").HasAttribute("disabled"));
        Assert.DoesNotContain(_live.Calls, c => c.StartsWith("recording"));
    }

    [Fact]
    public async Task Recording_link_shows_its_host_name()
    {
        var session = Seed();
        var page = await OpenAsync();
        page.Find($"[data-testid=live-recording-{session.Id}]").Click();
        Dialogs.WaitForElement("[data-testid=recording-url]", Wait);

        Dialogs.Find("input[data-testid=recording-url]").Input("https://drive.google.com/file/d/abc/view");

        Assert.Equal("drive.google.com", Dialogs.Find("[data-testid=recording-host] .lms-host-hint").TextContent.Trim());
        Dialogs.Find("[data-testid=recording-save]").Click();
        page.WaitForAssertion(() => Assert.Contains($"recording {session.Id}", _live.Calls), Wait);
        page.WaitForAssertion(() => Assert.Contains("Recording", page.Find($"[data-testid=live-row-{session.Id}]").TextContent), Wait);
    }

    [Fact]
    public async Task Provider_not_configured_shows_the_platform_admin_hint_without_a_settings_link()
    {
        _live.ProviderConfigured = false;
        var page = await OpenAsync();

        var banner = page.Find("[data-testid=live-provider-not-configured]");

        Assert.Contains("Ask your platform administrator", banner.TextContent);
        Assert.Empty(banner.QuerySelectorAll("a"));
        Assert.True(page.Find("[data-testid=live-schedule]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Provider_unavailable_shows_retry_after()
    {
        _live.ProviderHealthy = false; // Retry-After 300 s
        var page = await OpenAsync();
        Assert.NotEmpty(page.FindAll("[data-testid=live-provider-unhealthy]")); // scheduling stays possible
        await OpenDialogAsync(page);
        await FillBasicsAsync();

        SaveButton.Click();

        var error = Dialogs.WaitForElement("[data-testid=live-dialog-error]", Wait);
        Assert.Contains("Try again in about 5 min", error.TextContent);
    }

    [Fact]
    public async Task Provider_unavailable_below_a_minute_says_in_a_minute()
    {
        _live.ProviderHealthy = false;
        _live.ProviderRetryAfterSeconds = 20;
        var page = await OpenAsync();
        await OpenDialogAsync(page);
        await FillBasicsAsync();

        SaveButton.Click();

        Assert.Contains("in a minute", Dialogs.WaitForElement("[data-testid=live-dialog-error]", Wait).TextContent);
    }

    [Fact]
    public async Task Feature_disabled_shows_the_plain_message()
    {
        _live.FeatureEnabled = false;
        var page = await OpenAsync();
        await OpenDialogAsync(page);
        await FillBasicsAsync();

        SaveButton.Click();

        Assert.Contains("Live sessions are turned off for this school.", Dialogs.WaitForElement("[data-testid=live-dialog-error]", Wait).TextContent);
    }

    [Fact]
    public async Task Student_sees_no_authoring_controls()
    {
        _live.AsStudent = true;
        Seed();
        var page = await OpenAsync(Permissions.LiveSessions.View, Permissions.LessonPlans.View);

        Assert.NotEmpty(page.FindAll("[data-testid^=live-row-]"));
        Assert.Empty(page.FindAll("[data-testid=live-schedule]"));
        Assert.Empty(page.FindAll("[data-testid^=live-edit-]"));
        Assert.Empty(page.FindAll("[data-testid^=live-cancel-]"));
        Assert.Empty(page.FindAll("[data-testid^=live-recording-]"));
        Assert.Empty(page.FindAll("[data-testid=live-provider-not-configured]"));
    }

    [Fact]
    public async Task The_tab_is_shown_with_Manage_and_marked_current()
    {
        var page = await OpenAsync();

        var tab = page.Find("[data-testid=tab-live-sessions]");

        Assert.Equal("page", tab.GetAttribute("aria-current"));
    }

    [Fact]
    public async Task Upcoming_and_past_are_split_by_the_server_clock()
    {
        Seed(title: "Next week", daysFromNow: 7);
        Seed(LiveSessionStatuses.Ended, "Last week", daysFromNow: -7);
        var page = await OpenAsync();
        Assert.Contains("Next week", page.Markup);
        Assert.DoesNotContain("Last week", page.Markup);

        page.Find("[data-testid=live-filter-past]").Click();

        page.WaitForAssertion(() => Assert.Contains("Last week", page.Markup), Wait);
        Assert.DoesNotContain("Next week", page.Markup);
    }

    [Fact]
    public async Task Empty_upcoming_list_says_how_to_start()
    {
        var page = await OpenAsync();

        Assert.Contains("Schedule a session so students can join", page.Find("[data-testid=live-empty]").TextContent);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        Seed(title: "Weekly review");
        await GrantAsync(Permissions.LiveSessions.View, Permissions.LiveSessions.Manage, Permissions.LessonPlans.View);
        PlaceContextAtUnit();

        var layout = Render<MainLayout>(p => p.Add(l => l.Body,
            (RenderFragment)(b =>
            {
                b.OpenComponent<LiveSessionsPage>(0);
                b.AddComponentParameter(1, "Id", Cc);
                b.CloseComponent();
            })));

        layout.WaitForElement("[data-testid=live-title]", Wait);
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("الجلسات المباشرة", layout.Find("[data-testid=live-title]").TextContent);
    }
}
