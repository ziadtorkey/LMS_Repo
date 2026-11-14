using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Attendance;

namespace Lms.Client.Services.Attendance;

/// <summary>
/// In-memory attendance for <c>Lms:UseFakeApi=true</c> and the tests. It mimics the server rules the UI depends on: the edit window by the
/// (injected) server clock, the reason after the window, unmarked rows, a stale version, future sessions and the live proposal.
/// </summary>
public sealed class FakeAttendanceApi : IAttendanceApi
{
    public static readonly Guid SessionOpen = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid SessionTaken = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    public static readonly Guid SessionClosed = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    public static readonly Guid SessionProposal = Guid.Parse("a0000000-0000-0000-0000-000000000004");
    public static readonly Guid SessionSubstitute = Guid.Parse("a0000000-0000-0000-0000-000000000005");
    private static readonly Guid ClassCourse = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Teacher = Guid.Parse("e0000000-0000-0000-0000-000000000001");

    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;
    private readonly List<Row> _sessions = [];
    private readonly List<AttendanceChangeDto> _changes = [];
    private readonly (Guid Id, string Ar, string En, string Number)[] _roster =
    [
        (Guid.Parse("50000000-0000-0000-0000-000000000001"), "أحمد سعيد", "Ahmed Saeed", "S-1001"),
        (Guid.Parse("50000000-0000-0000-0000-000000000002"), "منى علي", "Mona Ali", "S-1002"),
        (Guid.Parse("50000000-0000-0000-0000-000000000003"), "خالد عمر", "Khaled Omar", "S-1003"),
        (Guid.Parse("50000000-0000-0000-0000-000000000004"), "ليلى حسن", "Layla Hassan", "S-1004"),
    ];

    public FakeAttendanceApi() : this(TimeProvider.System) { }

    /// <param name="clock">The server clock.</param>
    /// <param name="zone">The client time zone setting (D64: Asia/Riyadh by default); the server judges "today" by the client day, not the UTC day.</param>
    public FakeAttendanceApi(TimeProvider clock, TimeZoneInfo? zone = null)
    {
        _clock = clock;
        _zone = zone ?? ClientCalendar.Zone();
        var now = clock.GetUtcNow().UtcDateTime;
        var today = LocalToday();
        Add(SessionOpen, 1, AttendanceSources.Timetable, AttendanceSessionStates.Open, 0, now.AddHours(48), today);
        Add(SessionTaken, 2, AttendanceSources.Timetable, AttendanceSessionStates.Taken, 4, now.AddHours(24), today);
        Add(SessionClosed, 3, AttendanceSources.Timetable, AttendanceSessionStates.Taken, 4, now.AddHours(-1), today);
        Add(SessionProposal, 4, AttendanceSources.Live, AttendanceSessionStates.Open, 4, now.AddHours(48), today, proposal: true);
        Add(SessionSubstitute, 5, AttendanceSources.Timetable, AttendanceSessionStates.Open, 0, now.AddHours(48), today, substitute: true);
        _changes.Add(new AttendanceChangeDto(Guid.NewGuid(), _roster[0].Id, _roster[0].Ar, _roster[0].En, AttendanceChangeKinds.Taken, null, AttendanceStatuses.Present,
            null, Teacher, "أ. صالح", "A. Saleh", now.AddHours(-2)));
        _changes.Add(new AttendanceChangeDto(Guid.NewGuid(), _roster[1].Id, _roster[1].Ar, _roster[1].En, AttendanceChangeKinds.EditedAfterWindow, AttendanceStatuses.Absent,
            AttendanceStatuses.Excused, "Medical note received", Teacher, "أ. صالح", "A. Saleh", now.AddHours(-1)));
    }

    /// <summary>Whether the caller holds Attendance.Edit (edits after the window).</summary>
    public bool CanEditAfterWindow { get; set; }

    public IReadOnlyList<AttendanceSessionListItemDto> Sessions => _sessions.Select(s => s.Dto(_clock)).ToList();

    public Task<IReadOnlyList<AttendanceSessionListItemDto>> GetSessionsAsync(DateOnly? date = null, CancellationToken ct = default)
    {
        var day = date ?? LocalToday();
        return Task.FromResult<IReadOnlyList<AttendanceSessionListItemDto>>(_sessions.Where(s => s.Date == day).Select(s => s.Dto(_clock)).ToList());
    }

    public Task<PagedResult<AttendanceSessionListItemDto>> SearchSessionsAsync(Guid unitId, AttendanceSessionSearchQuery query, CancellationToken ct = default)
    {
        var rows = _sessions.Where(s => s.Date >= query.From && s.Date <= query.To && (query.State is null || s.State == query.State)).Select(s => s.Dto(_clock)).ToList();
        return Task.FromResult(new PagedResult<AttendanceSessionListItemDto>(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<AttendanceSessionListItemDto> CreateAdHocAsync(Guid classCourseId, CreateAdHocAttendanceSessionRequest request, CancellationToken ct = default)
    {
        var today = LocalToday();
        if (request.Date > today) throw Problem(409, "attendance_session_future");
        var row = Add(Guid.NewGuid(), 6, AttendanceSources.AdHoc, AttendanceSessionStates.Open, 0, _clock.GetUtcNow().UtcDateTime.AddHours(48), request.Date);
        return Task.FromResult(row.Dto(_clock));
    }

    public Task<AttendanceRecordsDto> GetRecordsAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult(Records(Find(sessionId)));

    public Task<AttendanceRecordsDto> TakeAsync(Guid sessionId, TakeAttendanceRequest request, CancellationToken ct = default)
    {
        var row = Find(sessionId);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (row.Date > LocalToday()) throw Problem(409, "attendance_session_future");
        if (request.Version != row.Version) throw Problem(409, "concurrency_conflict");
        var afterWindow = row.State == AttendanceSessionStates.Taken && now > row.EditableUntilUtc;
        if (afterWindow && !CanEditAfterWindow) throw Problem(409, "attendance_edit_window_closed");
        if (afterWindow && string.IsNullOrWhiteSpace(request.Reason)) throw Problem(422, "attendance_reason_required");

        var marks = row.Marks.ToDictionary(m => m.Key, m => m.Value);
        foreach (var (id, _, _, _) in _roster)
        {
            if (request.DefaultStatus is not null && !marks.ContainsKey(id)) marks[id] = request.DefaultStatus;
        }

        foreach (var record in request.Records) marks[record.StudentId] = record.Status;
        if (_roster.Any(r => !marks.ContainsKey(r.Id))) throw Problem(422, "attendance_unmarked_rows");

        foreach (var (id, status) in marks.Where(m => !row.Marks.TryGetValue(m.Key, out var old) || old != m.Value))
        {
            var student = _roster.First(r => r.Id == id);
            row.Marks.TryGetValue(id, out var from);
            _changes.Add(new AttendanceChangeDto(Guid.NewGuid(), id, student.Ar, student.En,
                afterWindow ? AttendanceChangeKinds.EditedAfterWindow : row.State == AttendanceSessionStates.Taken ? AttendanceChangeKinds.Edited : AttendanceChangeKinds.Taken,
                from, status, afterWindow ? request.Reason : null, Teacher, "أ. صالح", "A. Saleh", now));
        }

        row.Marks = marks;
        if (row.State == AttendanceSessionStates.Open) row.EditableUntilUtc = now.AddHours(48);
        row.State = AttendanceSessionStates.Taken;
        row.Version++;
        return Task.FromResult(Records(row));
    }

    public Task<IReadOnlyList<AttendanceChangeDto>> GetHistoryAsync(Guid sessionId, CancellationToken ct = default)
    {
        Find(sessionId);
        return Task.FromResult<IReadOnlyList<AttendanceChangeDto>>(_changes.OrderByDescending(c => c.ChangedAtUtc).ToList());
    }

    public Task<AttendanceRecordsDto> ConfirmAsync(Guid sessionId, ConfirmAttendanceRequest request, CancellationToken ct = default)
    {
        var row = Find(sessionId);
        if (row.Source != AttendanceSources.Live) throw Problem(409, "attendance_not_a_proposal");
        if (request.Version != row.Version) throw Problem(409, "concurrency_conflict");
        row.State = AttendanceSessionStates.Taken;
        row.Proposal = false;
        row.Version++;
        return Task.FromResult(Records(row));
    }

    public Task<AttendanceRecordsDto> SaveLiveMappingsAsync(Guid sessionId, LiveMappingRequest request, CancellationToken ct = default)
    {
        var row = Find(sessionId);
        if (row.Source != AttendanceSources.Live) throw Problem(409, "attendance_not_a_proposal");
        return Task.FromResult(Records(row));
    }

    /// <summary>Set to false to see a student under GuardiansOnly (no excuse action, the guardian sentence instead).</summary>
    public bool CanSubmitExcuse { get; set; } = true;

    public Task<PagedResult<AttendanceAlertDto>> GetAlertsAsync(AttendanceAlertsQuery query, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var unit = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        AttendanceAlertDto Alert(int i, int absences, int lates, DateTime? cleared) =>
            new(Guid.NewGuid(), _roster[i].Id, _roster[i].Ar, _roster[i].En, _roster[i].Number, unit, absences, lates, 30, absences + lates * 0.5m, 5m, now.AddDays(-i), cleared);
        List<AttendanceAlertDto> all = [Alert(1, 5, 1, null), Alert(2, 4, 3, null), Alert(0, 6, 0, now.AddDays(-1))];
        var rows = all.Where(a => !query.ActiveOnly || a.ClearedAtUtc is null).ToList();
        return Task.FromResult(new PagedResult<AttendanceAlertDto>(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<AttendanceReportDto> GetReportAsync(string kind, AttendanceReportQuery query, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var today = LocalToday();
        var rows = _roster.Select((r, i) => new AttendanceReportRowDto(r.Id, r.Ar, r.En, r.Number, null, null, null,
            new AttendanceSummaryDto(18 - i, i, i % 2, 0, 19 + (i % 2), 90m - (i * 5)))).ToList();
        var total = new AttendanceSummaryDto(rows.Sum(r => r.Summary.Present), rows.Sum(r => r.Summary.Absent), rows.Sum(r => r.Summary.Late), 0, rows.Sum(r => r.Summary.Total), 84.5m);
        return Task.FromResult(new AttendanceReportDto(kind, query.From ?? today.AddDays(-30), query.To ?? today, total,
            new PagedResult<AttendanceReportRowDto>(rows, rows.Count, query.Page, query.PageSize), now.AddMinutes(-3), now));
    }

    public Task<(Stream Content, string FileName)> ExportReportAsync(string kind, AttendanceReportQuery query, CancellationToken ct = default) =>
        Task.FromResult<(Stream, string)>((new MemoryStream([0x50, 0x4B, 0x03, 0x04]), $"attendance-{kind}.xlsx"));

    public Task<OwnAttendanceDto> GetMyAttendanceAsync(OwnAttendanceQuery query, CancellationToken ct = default) => Task.FromResult(Own(_roster[0].Id, query));

    public Task<OwnAttendanceDto> GetStudentAttendanceAsync(Guid studentId, OwnAttendanceQuery query, CancellationToken ct = default)
    {
        if (_roster.All(r => r.Id != studentId)) throw Problem(404, "student_not_found");
        return Task.FromResult(Own(studentId, query));
    }

    public static readonly Guid AbsentSession = Guid.Parse("a0000000-0000-0000-0000-000000000011");
    public static readonly Guid LateSession = Guid.Parse("a0000000-0000-0000-0000-000000000012");
    public static readonly Guid ExcusedSession = Guid.Parse("a0000000-0000-0000-0000-000000000013");
    public static readonly Guid PendingSession = Guid.Parse("a0000000-0000-0000-0000-000000000014");

    private OwnAttendanceDto Own(Guid studentId, OwnAttendanceQuery query)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var today = LocalToday();
        var student = _roster.First(r => r.Id == studentId);
        OwnAttendanceItemDto Item(Guid id, int daysAgo, string en, string ar, string status, bool excusable, Guid? excuse = null, string? excuseStatus = null) =>
            new(id, today.AddDays(-daysAgo), ar, en, new TimeOnly(7, 30), new TimeOnly(8, 15), status, null, excusable && CanSubmitExcuseFlag, excuse, excuseStatus);
        List<OwnAttendanceItemDto> items =
        [
            Item(AbsentSession, 1, "Mathematics", "الرياضيات", AttendanceStatuses.Absent, true),
            Item(LateSession, 2, "Science", "العلوم", AttendanceStatuses.Late, true),
            Item(PendingSession, 3, "Arabic", "اللغة العربية", AttendanceStatuses.Absent, false, Guid.NewGuid(), "Submitted"),
            Item(ExcusedSession, 5, "English", "اللغة الإنجليزية", AttendanceStatuses.Excused, false, Guid.NewGuid(), "Approved"),
            Item(Guid.NewGuid(), 6, "Mathematics", "الرياضيات", AttendanceStatuses.Present, false),
        ];
        var summary = new AttendanceSummaryDto(1, 2, 1, 1, 5, 80m);
        return new OwnAttendanceDto(studentId, student.Ar, student.En, query.From ?? today.AddDays(-30), query.To ?? today, summary,
            new PagedResult<OwnAttendanceItemDto>(items, items.Count, query.Page, query.PageSize), CanSubmitExcuse, now);
    }

    private bool CanSubmitExcuseFlag => CanSubmitExcuse;

    /// <summary>The client's local day by the server clock.</summary>
    private DateOnly LocalToday() => ClientCalendar.Today(_clock.GetUtcNow().UtcDateTime, _zone);

    private Row Add(Guid id, int period, string source, string state, int marked, DateTime editableUntil, DateOnly date, bool proposal = false, bool substitute = false)
    {
        var row = new Row(id, period, source, state, date, editableUntil, proposal, substitute)
        {
            Marks = _roster.Take(marked).ToDictionary(r => r.Id, _ => AttendanceStatuses.Present),
        };
        _sessions.Add(row);
        return row;
    }

    private Row Find(Guid id) => _sessions.FirstOrDefault(s => s.Id == id) ?? throw Problem(404, "attendance_session_not_found");

    private AttendanceRecordsDto Records(Row row) => new(
        row.Dto(_clock),
        _roster.Select(r => new AttendanceRecordDto(r.Id, r.Ar, r.En, r.Number, row.Marks.GetValueOrDefault(r.Id), null,
            row.Proposal ? 42 : null, row.Proposal ? _clock.GetUtcNow().UtcDateTime.AddHours(-1) : null, row.Proposal)).ToList(),
        [],
        true);

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    private sealed class Row(Guid id, int period, string source, string state, DateOnly date, DateTime editableUntilUtc, bool proposal, bool substitute)
    {
        public Guid Id { get; } = id;
        public int Period { get; } = period;
        public string Source { get; } = source;
        public string State { get; set; } = state;
        public DateOnly Date { get; } = date;
        public DateTime EditableUntilUtc { get; set; } = editableUntilUtc;
        public bool Proposal { get; set; } = proposal;
        public bool Substitute { get; } = substitute;
        public int Version { get; set; } = 1;
        public Dictionary<Guid, string> Marks { get; set; } = [];

        public AttendanceSessionListItemDto Dto(TimeProvider clock) => new(
            Id, ClassCourse, "الرياضيات", "Mathematics", "الصف 5أ", "Class 5A", Date, null,
            new TimeOnly(7, 30).AddMinutes(50 * (Period - 1)), new TimeOnly(8, 15).AddMinutes(50 * (Period - 1)),
            Source, State, Marks.Count, 4, State == AttendanceSessionStates.Taken ? EditableUntilUtc : null, Substitute, Proposal, Version, clock.GetUtcNow().UtcDateTime);
    }
}
