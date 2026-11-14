using System.Globalization;
using Bunit;
using Lms.Client.Pages.Attendance;
using Lms.Client.Services.Api;
using Lms.Client.Services.Attendance;
using Lms.Client.Tests.Support;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Attendance;

public sealed class AttendanceTodayTests : StaffTestContext
{
    private readonly TestClock _clock = new();
    private readonly FakeAttendanceApi _api;

    public AttendanceTodayTests()
    {
        _api = new FakeAttendanceApi(_clock);
        Services.AddSingleton<IAttendanceApi>(_api);
    }

    private async Task<IRenderedComponent<AttendanceTodayPage>> RenderPageAsync(params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.Attendance.Take] : permissions);
        RenderProviders();
        var page = Render<AttendanceTodayPage>();
        page.WaitForElement("[data-testid=attendance-sessions]");
        return page;
    }

    // ----- the fake mimics the server rules the UI relies on -----

    [Fact]
    public async Task Fake_edit_window_uses_the_server_clock()
    {
        var taken = FakeAttendanceApi.SessionTaken;
        var records = await _api.GetRecordsAsync(taken);
        var request = new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, records.Session.Version);

        await _api.TakeAsync(taken, request); // inside the window: allowed
        _clock.Advance(TimeSpan.FromHours(30)); // the window (24 h left) is over by the server's clock

        var again = new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, (await _api.GetRecordsAsync(taken)).Session.Version);
        Assert.Equal("attendance_edit_window_closed", (await Assert.ThrowsAsync<ApiProblemException>(() => _api.TakeAsync(taken, again))).Code);

        _api.CanEditAfterWindow = true;
        Assert.Equal("attendance_reason_required", (await Assert.ThrowsAsync<ApiProblemException>(() => _api.TakeAsync(taken, again))).Code);
        await _api.TakeAsync(taken, again with { DefaultStatus = null, Records = [new TakeAttendanceRecord(Guid.Parse("50000000-0000-0000-0000-000000000001"), AttendanceStatuses.Late)], Reason = "Late correction" });
        Assert.Contains(await _api.GetHistoryAsync(taken), c => c.Reason == "Late correction" && c.Kind == AttendanceChangeKinds.EditedAfterWindow && c.ToStatus == AttendanceStatuses.Late);
    }

    [Fact]
    public async Task Fake_rejects_unmarked_rows_a_stale_version_and_a_future_ad_hoc_session()
    {
        var open = FakeAttendanceApi.SessionOpen;
        var version = (await _api.GetRecordsAsync(open)).Session.Version;

        Assert.Equal("attendance_unmarked_rows", (await Assert.ThrowsAsync<ApiProblemException>(() => _api.TakeAsync(open, new TakeAttendanceRequest(null, [], null, version)))).Code);
        Assert.Equal("concurrency_conflict", (await Assert.ThrowsAsync<ApiProblemException>(() => _api.TakeAsync(open, new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, version + 5)))).Code);

        var tomorrow = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(1);
        Assert.Equal("attendance_session_future", (await Assert.ThrowsAsync<ApiProblemException>(() => _api.CreateAdHocAsync(Guid.NewGuid(), new CreateAdHocAttendanceSessionRequest(tomorrow)))).Code);
    }

    // ----- the page -----

    [Fact]
    public async Task Cards_show_state_progress_and_one_primary_action()
    {
        Culture.Current = new CultureInfo("en");
        var page = await RenderPageAsync();

        var open = FakeAttendanceApi.SessionOpen;
        Assert.Contains("Open", page.Find($"[data-testid=session-state-{open}]").TextContent);
        Assert.Contains("0 of 4 marked", page.Find($"[data-testid=session-progress-{open}]").TextContent);
        Assert.Contains("Take attendance", page.Find($"[data-testid=session-primary-{open}]").TextContent);

        var taken = FakeAttendanceApi.SessionTaken;
        Assert.Contains("Taken", page.Find($"[data-testid=session-state-{taken}]").TextContent);
        Assert.Contains("Review", page.Find($"[data-testid=session-primary-{taken}]").TextContent);
        Assert.Single(page.FindAll($"[data-testid=session-{taken}] .mud-button-filled"));
    }

    [Fact]
    public async Task Edit_window_closed_chip_and_edit_only_with_permission()
    {
        Culture.Current = new CultureInfo("en");
        var closed = FakeAttendanceApi.SessionClosed;

        var teacher = await RenderPageAsync(Permissions.Attendance.Take);
        Assert.Contains("Edit window closed", teacher.Find($"[data-testid=session-locked-{closed}]").TextContent);
        Assert.Empty(teacher.FindAll($"[data-testid=session-primary-{closed}]")); // no Attendance.Edit: nothing to press
        await DisposeComponentsAsync();

        var counselor = await RenderPageAsync(Permissions.Attendance.Take, Permissions.Attendance.Edit);
        Assert.Contains("Edit attendance", counselor.Find($"[data-testid=session-primary-{closed}]").TextContent);
    }

    [Fact]
    public async Task Substitute_sessions_are_listed_with_a_banner()
    {
        Culture.Current = new CultureInfo("en");
        var page = await RenderPageAsync();

        Assert.Contains("substitute", page.Find($"[data-testid=session-substitute-{FakeAttendanceApi.SessionSubstitute}]").TextContent);
        Assert.Empty(page.FindAll($"[data-testid=session-substitute-{FakeAttendanceApi.SessionOpen}]"));
    }

    [Fact]
    public async Task Proposed_from_live_session_chip_and_confirm_action()
    {
        Culture.Current = new CultureInfo("en");
        var page = await RenderPageAsync();

        var proposal = FakeAttendanceApi.SessionProposal;
        Assert.Contains("Proposed from the live session", page.Find($"[data-testid=session-proposal-{proposal}]").TextContent);
        Assert.Contains("Confirm attendance", page.Find($"[data-testid=session-primary-{proposal}]").TextContent);
    }

    [Fact]
    public async Task History_drawer_lists_changes()
    {
        Culture.Current = new CultureInfo("en");
        var page = await RenderPageAsync();

        page.Find($"[data-testid=session-history-{FakeAttendanceApi.SessionTaken}]").Click();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid=history-item]").Count));
        Assert.Contains("Medical note received", page.Find("[data-testid=history-list]").TextContent);
        Assert.Contains("Edited after the window", page.Find("[data-testid=history-list]").TextContent);
    }

    [Fact]
    public async Task Without_Attendance_Take_the_page_is_forbidden()
    {
        await GrantAsync(Permissions.Attendance.View);
        RenderProviders();

        var page = Render<AttendanceTodayPage>();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=attendance-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderPageAsync();

        Assert.Equal("الحضور", page.Find("[data-testid=attendance-title]").TextContent.Trim());
        Assert.Contains("مراجعة", page.Find($"[data-testid=session-primary-{FakeAttendanceApi.SessionTaken}]").TextContent);
        Assert.All(page.FindAll("[data-testid^=session-time-]"), t => Assert.Equal("ltr", t.GetAttribute("dir"))); // times stay left to right
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
