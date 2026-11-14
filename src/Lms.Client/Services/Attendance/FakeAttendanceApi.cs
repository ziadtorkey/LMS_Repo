using Lms.Client.Services.Api;
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

    public FakeAttendanceApi(TimeProvider clock)
    {
        _clock = clock;
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
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
        var day = date ?? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        return Task.FromResult<IReadOnlyList<AttendanceSessionListItemDto>>(_sessions.Where(s => s.Date == day).Select(s => s.Dto(_clock)).ToList());
    }

    public Task<AttendanceSessionListItemDto> CreateAdHocAsync(Guid classCourseId, CreateAdHocAttendanceSessionRequest request, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (request.Date > today) throw Problem(409, "attendance_session_future");
        var row = Add(Guid.NewGuid(), 6, AttendanceSources.AdHoc, AttendanceSessionStates.Open, 0, _clock.GetUtcNow().UtcDateTime.AddHours(48), request.Date);
        return Task.FromResult(row.Dto(_clock));
    }

    public Task<AttendanceRecordsDto> GetRecordsAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult(Records(Find(sessionId)));

    public Task<AttendanceRecordsDto> TakeAsync(Guid sessionId, TakeAttendanceRequest request, CancellationToken ct = default)
    {
        var row = Find(sessionId);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (row.Date > DateOnly.FromDateTime(now)) throw Problem(409, "attendance_session_future");
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
