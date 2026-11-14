using System.Globalization;
using Bunit;
using Lms.Client.Pages.Attendance;
using Lms.Client.Services.Api;
using Lms.Client.Services.Attendance;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Attendance;

public sealed class AttendanceTakePageTests : StaffTestContext
{
    private static readonly Guid Ahmed = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Mona = Guid.Parse("50000000-0000-0000-0000-000000000002");
    private readonly Recording _api;
    private readonly StorageJs _js = new();
    private readonly FakeAttendanceApi _fake = new();

    public AttendanceTakePageTests()
    {
        _api = new Recording(_fake);
        Services.AddSingleton<IAttendanceApi>(_api);
        Services.AddSingleton(new AttendanceDraftStore(_js, TimeProvider.System));
    }

    private async Task<IRenderedComponent<AttendanceTakePage>> RenderPageAsync(Guid session, params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Attendance.Take] : permissions);
        RenderProviders();
        var page = Render<AttendanceTakePage>(p => p.Add(x => x.Id, session));
        page.WaitForElement("[data-testid=att-roster]");
        return page;
    }

    private static string Status(Guid student, string status) => $"[data-testid=att-{student}-{status}]";

    [Fact]
    public async Task Mark_all_present_then_one_exception_sends_default_plus_exception()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        page.Find("[data-testid=att-mark-all]").Click();
        page.Find(Status(Mona, "Absent")).Click();
        page.Find("[data-testid=att-save]").Click();

        page.WaitForAssertion(() => Assert.NotNull(_api.LastTake));
        Assert.Equal(AttendanceStatuses.Present, _api.LastTake!.DefaultStatus);
        var exception = Assert.Single(_api.LastTake.Records);
        Assert.Equal((Mona, AttendanceStatuses.Absent), (exception.StudentId, exception.Status));
        page.WaitForElement("[data-testid=att-saved]");
    }

    [Fact]
    public async Task Digit_keys_1_to_4_set_the_focused_row_only()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        await page.InvokeAsync(() => page.Instance.OnDigit(Mona.ToString(), 3));

        Assert.Equal("true", page.Find(Status(Mona, "Late")).GetAttribute("aria-checked"));
        Assert.Equal("false", page.Find(Status(Ahmed, "Late")).GetAttribute("aria-checked"));
        Assert.Equal("3", page.Find("[data-testid=att-count-unmarked] strong").TextContent.Trim()); // the other three are untouched
        await page.InvokeAsync(() => page.Instance.OnDigit(Mona.ToString(), 9)); // out of range: ignored
        Assert.Equal("true", page.Find(Status(Mona, "Late")).GetAttribute("aria-checked"));
    }

    [Fact]
    public async Task Buttons_show_text_and_digit_hints()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        var buttons = page.FindAll($"[data-testid=att-row-{Ahmed}] [role=radio]");
        Assert.Equal(4, buttons.Count);
        Assert.Equal(["Present", "Absent", "Late", "Excused"], buttons.Select(b => b.QuerySelector(".lms-att-label")!.TextContent.Trim()).ToList());
        Assert.Equal(["1", "2", "3", "4"], buttons.Select(b => b.QuerySelector("kbd")!.TextContent.Trim()).ToList());
        Assert.Contains("half an absence", page.Find("[data-testid=att-late-rule]").TextContent); // D61, shown where Late is marked
    }

    [Fact]
    public async Task Unmarked_rows_cannot_be_saved()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        Assert.True(page.Find("[data-testid=att-save]").HasAttribute("disabled"));
        Assert.NotNull(page.Find($"[data-testid=att-unmarked-{Ahmed}]"));
        page.Find("[data-testid=att-mark-all]").Click();
        Assert.False(page.Find("[data-testid=att-save]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Future_session_is_disabled_with_the_reason()
    {
        _api.Mutate = data => data with { Session = data.Session with { Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), ServerNowUtc = DateTime.UtcNow } };
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        Assert.Contains("has not happened yet", page.Find("[data-testid=att-future]").TextContent);
        Assert.True(page.Find(Status(Ahmed, "Present")).HasAttribute("disabled"));
        Assert.True(page.Find("[data-testid=att-mark-all]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_session_of_the_local_day_is_not_future_at_01_00_in_Riyadh()
    {
        // 01:00 on 15 Nov in Riyadh is 22:00 UTC on 14 Nov: the session day (15 Nov, a client-zone day) must not look like the future.
        var nowUtc = new DateTime(2026, 11, 14, 22, 0, 0, DateTimeKind.Utc);
        _api.Mutate = data => data with { Session = data.Session with { Date = new DateOnly(2026, 11, 15), ServerNowUtc = nowUtc } };
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        Assert.Empty(page.FindAll("[data-testid=att-future]"));
        Assert.False(page.Find(Status(Ahmed, "Present")).HasAttribute("disabled"));
        await DisposeComponentsAsync();

        // the day after is still the future
        _api.Mutate = data => data with { Session = data.Session with { Date = new DateOnly(2026, 11, 16), ServerNowUtc = nowUtc } };
        var future = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        Assert.NotNull(future.Find("[data-testid=att-future]"));
    }

    [Fact]
    public async Task Next_unmarked_moves_focus_to_the_first_unmarked_row()
    {
        var module = JSInterop.SetupModule("./js/attendance-keys.js");
        module.Mode = JSRuntimeMode.Loose;
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        await page.InvokeAsync(() => page.Instance.OnDigit(Ahmed.ToString(), 1)); // the first row is marked: the second is the first unmarked

        page.Find("[data-testid=att-next]").Click();

        var call = module.Invocations["focusRow"].Last();
        Assert.Equal(Mona.ToString(), call.Arguments[1]?.ToString());
    }

    [Fact]
    public async Task The_selected_status_has_a_check_mark_and_a_selected_class_not_colour_alone()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        page.Find(Status(Mona, "Late")).Click();

        Assert.NotNull(page.Find($"[data-testid=att-tick-{Mona}-Late]")); // the check mark appears on the chosen one only
        Assert.Contains("lms-att-selected", page.Find(Status(Mona, "Late")).ClassName); // a 2 px border in the status colour (CSS)
        Assert.Empty(page.FindAll($"[data-testid=att-tick-{Mona}-Present]"));
        Assert.Empty(page.FindAll($"[data-testid=att-tick-{Ahmed}-Late]"));
    }

    [Fact]
    public async Task Mark_all_present_is_filled_says_how_many_and_can_be_undone()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        page.Find(Status(Ahmed, "Absent")).Click(); // one row already marked

        Assert.Contains("mud-button-filled", page.Find("[data-testid=att-mark-all]").ClassName);
        page.Find("[data-testid=att-mark-all]").Click();

        var message = page.Find("[data-testid=att-mark-all-message]").TextContent;
        Assert.Contains("3 students marked present", message); // only the unmarked ones
        Assert.Contains("kept their status", message);
        Assert.Equal("true", page.Find(Status(Ahmed, "Absent")).GetAttribute("aria-checked"));
        Assert.Equal("0", page.Find("[data-testid=att-count-unmarked] strong").TextContent.Trim());

        page.Find("[data-testid=att-mark-all-undo]").Click();

        Assert.Equal("3", page.Find("[data-testid=att-count-unmarked] strong").TextContent.Trim()); // back to before
        Assert.Empty(page.FindAll("[data-testid=att-mark-all-message]"));
    }

    [Fact]
    public async Task Notes_are_collapsed_behind_Add_note()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        page.Find(Status(Mona, "Absent")).Click();
        Assert.Empty(page.FindAll($"[data-testid=att-note-{Mona}]")); // no full-width field on a phone roster
        page.Find($"[data-testid=att-note-add-{Mona}]").Click();

        Assert.NotNull(page.Find($"[data-testid=att-note-{Mona}]"));
        Assert.Empty(page.FindAll($"[data-testid=att-note-add-{Mona}]"));
    }

    [Fact]
    public async Task The_draft_banner_is_a_warning_because_it_is_unsaved_work()
    {
        var store = new AttendanceDraftStore(_js, TimeProvider.System);
        var version = (await _fake.GetRecordsAsync(FakeAttendanceApi.SessionOpen)).Session.Version;
        await store.SaveAsync(FakeAttendanceApi.SessionOpen, version, new Dictionary<Guid, string> { [Ahmed] = "Absent" });

        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        Assert.Contains("mud-alert-outlined-warning", page.Find("[data-testid=att-draft-banner]").ClassName);
    }

    [Fact]
    public async Task A_View_holder_reads_the_session_but_cannot_mark_inside_the_window()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen, Permissions.Attendance.View);

        Assert.NotNull(page.Find("[data-testid=att-read-only]"));
        Assert.True(page.Find(Status(Ahmed, "Present")).HasAttribute("disabled"));
        Assert.True(page.Find("[data-testid=att-mark-all]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_clients_Org_TimeZone_decides_what_is_future()
    {
        // 01:00 on 15 Nov in Riyadh is 22:00 UTC on the 14th; for a client in UTC the 15th is still tomorrow.
        var nowUtc = new DateTime(2026, 11, 14, 22, 0, 0, DateTimeKind.Utc);
        _api.Mutate = data => data with { Session = data.Session with { Date = new DateOnly(2026, 11, 15), ServerNowUtc = nowUtc } };
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Attendance.Take);
        var active = Context.ActiveAssignmentId!.Value;
        Api.Me = Api.Me with { Contexts = [new Lms.Shared.Me.UserContextDto(active, Guid.NewGuid(), "م", "School", Guid.NewGuid(), "ص", "Grade", "م", "Teacher", "Etc/UTC")] };
        RenderProviders();

        var page = Render<AttendanceTakePage>(p => p.Add(x => x.Id, FakeAttendanceApi.SessionOpen));
        page.WaitForElement("[data-testid=att-roster]");

        Assert.NotNull(page.Find("[data-testid=att-future]"));
    }

    [Fact]
    public async Task Reason_required_after_the_window()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionClosed, Permissions.Attendance.Take, Permissions.Attendance.Edit);
        page.Find(Status(Ahmed, "Late")).Click();

        page.Find("[data-testid=att-save]").Click();
        Assert.Null(_api.LastTake); // nothing is sent without a reason
        Assert.Contains("reason", page.Find("[data-testid=att-reason]").ParentElement!.TextContent, StringComparison.OrdinalIgnoreCase);

        page.Find("[data-testid=att-reason] input, input[data-testid=att-reason]").Change("Late arrival confirmed by the teacher");
        page.Find("[data-testid=att-save]").Click();
        page.WaitForAssertion(() => Assert.Equal("Late arrival confirmed by the teacher", _api.LastTake?.Reason));
    }

    [Fact]
    public async Task Without_Attendance_Edit_a_closed_session_is_read_only()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionClosed);

        Assert.NotNull(page.Find("[data-testid=att-window-closed]"));
        Assert.True(page.Find(Status(Ahmed, "Late")).HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-testid=att-reason]"));
    }

    [Fact]
    public async Task Stale_draft_is_not_offered_and_a_current_one_can_be_restored()
    {
        var store = new AttendanceDraftStore(_js, TimeProvider.System);
        var version = (await _fake.GetRecordsAsync(FakeAttendanceApi.SessionOpen)).Session.Version;
        await store.SaveAsync(FakeAttendanceApi.SessionOpen, version + 1, new Dictionary<Guid, string> { [Ahmed] = "Absent" }); // made against another version
        var stale = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        Assert.Empty(stale.FindAll("[data-testid=att-draft-banner]"));
        await DisposeComponentsAsync();

        await store.SaveAsync(FakeAttendanceApi.SessionOpen, version, new Dictionary<Guid, string> { [Ahmed] = "Absent" });
        var current = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        current.Find("[data-testid=att-draft-restore]").Click();

        Assert.Equal("true", current.Find(Status(Ahmed, "Absent")).GetAttribute("aria-checked"));
        Assert.Empty(current.FindAll("[data-testid=att-draft-banner]"));
    }

    [Fact]
    public async Task Marks_are_kept_as_a_draft_and_the_draft_is_cleared_on_save()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);

        page.Find("[data-testid=att-mark-all]").Click();
        Assert.Single(_js.Items); // only ids and statuses, in the browser

        page.Find("[data-testid=att-save]").Click();
        page.WaitForElement("[data-testid=att-saved]");
        Assert.Empty(_js.Items);
    }

    [Fact]
    public async Task Save_failure_keeps_marks_and_offers_retry()
    {
        _api.FailNextTake = new HttpRequestException("offline");
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        page.Find("[data-testid=att-mark-all]").Click();
        page.Find(Status(Ahmed, "Late")).Click();

        page.Find("[data-testid=att-save]").Click();
        page.WaitForElement("[data-testid=att-save-error]");
        Assert.Equal("true", page.Find(Status(Ahmed, "Late")).GetAttribute("aria-checked")); // the marks stay
        Assert.Single(_js.Items); // and so does the draft

        page.Find("[data-testid=att-save-retry]").Click();
        page.WaitForElement("[data-testid=att-saved]");
    }

    [Fact]
    public async Task Concurrency_conflict_reloads()
    {
        _api.FailNextTake = new ApiProblemException(409, "concurrency_conflict", null, null);
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        page.Find("[data-testid=att-mark-all]").Click();

        page.Find("[data-testid=att-save]").Click();

        page.WaitForElement("[data-testid=att-conflict]");
        Assert.Equal("4", page.Find("[data-testid=att-count-unmarked] strong").TextContent.Trim()); // the newer server state is shown, the local marks are gone
        Assert.Empty(_js.Items);
    }

    [Fact]
    public async Task Next_unmarked_is_disabled_once_everyone_is_marked()
    {
        var page = await RenderPageAsync(FakeAttendanceApi.SessionOpen);
        Assert.False(page.Find("[data-testid=att-next]").HasAttribute("disabled"));

        page.Find("[data-testid=att-mark-all]").Click();

        Assert.True(page.Find("[data-testid=att-next]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        await GrantAsync(Permissions.Attendance.Take);
        RenderProviders();
        Culture.Current = new CultureInfo("ar");
        var page = Render<AttendanceTakePage>(p => p.Add(x => x.Id, FakeAttendanceApi.SessionOpen));
        page.WaitForElement("[data-testid=att-roster]");

        Assert.Equal("الرياضيات", page.Find("[data-testid=take-title]").TextContent.Trim());
        Assert.Equal("حاضر", page.Find(Status(Ahmed, "Present")).QuerySelector(".lms-att-label")!.TextContent.Trim());
        Assert.Contains("نصف غياب", page.Find("[data-testid=att-late-rule]").TextContent);
    }

    /// <summary>Wraps the fake to see what the page sends and to inject the failures the page must survive.</summary>
    private sealed class Recording(FakeAttendanceApi inner) : IAttendanceApi
    {
        public TakeAttendanceRequest? LastTake { get; private set; }
        public Exception? FailNextTake { get; set; }
        public Func<AttendanceRecordsDto, AttendanceRecordsDto>? Mutate { get; set; }

        public Task<IReadOnlyList<AttendanceSessionListItemDto>> GetSessionsAsync(DateOnly? date = null, CancellationToken ct = default) => inner.GetSessionsAsync(date, ct);
        public Task<PagedResult<AttendanceSessionListItemDto>> SearchSessionsAsync(Guid unitId, AttendanceSessionSearchQuery query, CancellationToken ct = default) => inner.SearchSessionsAsync(unitId, query, ct);
        public Task<AttendanceSessionListItemDto> CreateAdHocAsync(Guid classCourseId, CreateAdHocAttendanceSessionRequest request, CancellationToken ct = default) => inner.CreateAdHocAsync(classCourseId, request, ct);

        public async Task<AttendanceRecordsDto> GetRecordsAsync(Guid sessionId, CancellationToken ct = default)
        {
            var data = await inner.GetRecordsAsync(sessionId, ct);
            return Mutate is null ? data : Mutate(data);
        }

        public Task<AttendanceRecordsDto> TakeAsync(Guid sessionId, TakeAttendanceRequest request, CancellationToken ct = default)
        {
            if (FailNextTake is { } failure)
            {
                FailNextTake = null;
                return Task.FromException<AttendanceRecordsDto>(failure);
            }

            LastTake = request;
            return inner.TakeAsync(sessionId, request, ct);
        }

        public Task<IReadOnlyList<AttendanceChangeDto>> GetHistoryAsync(Guid sessionId, CancellationToken ct = default) => inner.GetHistoryAsync(sessionId, ct);
        public Task<AttendanceRecordsDto> ConfirmAsync(Guid sessionId, ConfirmAttendanceRequest request, CancellationToken ct = default) => inner.ConfirmAsync(sessionId, request, ct);
        public Task<AttendanceRecordsDto> SaveLiveMappingsAsync(Guid sessionId, LiveMappingRequest request, CancellationToken ct = default) => inner.SaveLiveMappingsAsync(sessionId, request, ct);
        public Task<OwnAttendanceDto> GetMyAttendanceAsync(OwnAttendanceQuery query, CancellationToken ct = default) => inner.GetMyAttendanceAsync(query, ct);
        public Task<OwnAttendanceDto> GetStudentAttendanceAsync(Guid studentId, OwnAttendanceQuery query, CancellationToken ct = default) => inner.GetStudentAttendanceAsync(studentId, query, ct);
    }
}
