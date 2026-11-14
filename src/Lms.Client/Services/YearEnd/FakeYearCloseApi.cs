using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.YearEnd;

namespace Lms.Client.Services.YearEnd;

internal static class FakeProblems
{
    public static ApiProblemException Of(int status, string? code, string? field = null, IReadOnlyList<string>? codes = null) =>
        new(status, code, null, null, field, codes: codes);

    public static PagedResult<T> Page<T>(IEnumerable<T> source, int page, int pageSize)
    {
        var all = source.ToList();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, Paging.MaxPageSize);
        return new PagedResult<T>([.. all.Skip((page - 1) * pageSize).Take(pageSize)], all.Count, page, pageSize);
    }
}

/// <summary>
/// In-memory <see cref="IYearCloseApi"/>: closing needs the year's name typed (400 on <c>ConfirmName</c>), a blocker refuses it (409 <c>year_close_blocked</c>), a closed year
/// cannot be closed again (409 <c>year_closed</c>), unpublished grades only warn (D79), and a closing report exists only after the close.
/// </summary>
public sealed class FakeYearCloseApi : IYearCloseApi
{
    public static readonly Guid DemoYearId = Guid.Parse("00000000-0000-0000-0000-0000000f0001");
    public const string DemoNameEn = "2026/2027";
    public const string DemoNameAr = "٢٠٢٦/٢٠٢٧";

    private readonly HashSet<Guid> _closed = [];

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <summary>Blocking issues the next close reports (a test or demo sets them).</summary>
    public List<YearEndIssueDto> Blocking { get; } = [];

    /// <summary>Warnings of the preview; unpublished grades warn but never block (D79).</summary>
    public List<YearEndIssueDto> Warnings { get; } = [new(YearEndIssueCodes.UnpublishedGradeItems, 3, null)];

    public bool IsClosed(Guid yearId) => _closed.Contains(yearId);

    public Task<YearClosePreviewDto> GetPreviewAsync(Guid yearId, CancellationToken ct = default) =>
        Task.FromResult(new YearClosePreviewDto(yearId, DemoNameAr, DemoNameEn, 12, 480, [.. Blocking], [.. Warnings]));

    public Task<YearClosedDto> CloseAsync(Guid yearId, CloseYearRequest request, CancellationToken ct = default)
    {
        if (IsClosed(yearId)) throw FakeProblems.Of(409, ProblemCodes.year_closed);
        var name = request.ConfirmName?.Trim();
        if (!string.Equals(name, DemoNameEn, StringComparison.OrdinalIgnoreCase) && name != DemoNameAr)
            throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(CloseYearRequest.ConfirmName));
        if (Blocking.Count > 0) throw FakeProblems.Of(409, ProblemCodes.year_close_blocked, codes: [.. Blocking.Select(b => b.Code)]);
        _closed.Add(yearId);
        var now = Now();
        return Task.FromResult(new YearClosedDto(yearId, now, 12, 480, now));
    }

    public Task<ClosingReportDto> GetReportAsync(Guid yearId, CancellationToken ct = default)
    {
        if (!IsClosed(yearId)) throw FakeProblems.Of(409, ProblemCodes.year_not_closed);
        return Task.FromResult(new ClosingReportDto(yearId, Now(),
            [new(Guid.NewGuid(), "الصف الأول أ", "Grade 1 A", 24, 6, 6, 240), new(Guid.NewGuid(), "الصف الأول ب", "Grade 1 B", 22, 6, 5, 240)]));
    }
}

/// <summary>
/// In-memory <see cref="IGradeChangesApi"/>: a request needs a locked gradebook (403 <c>grade_change_not_allowed</c>), a 20 to 500 character reason (400 on <c>Reason</c>),
/// one open request per grade (409 <c>grade_change_pending</c>); an approver reads all, a requester only their own; the requester never decides their own (D80); a rejection needs a note.
/// </summary>
public sealed class FakeGradeChangesApi : IGradeChangesApi
{
    private readonly List<GradeChangeRequestDto> _requests = [];

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <summary>The signed-in person; set it to switch between the teacher and the approver.</summary>
    public Guid Me { get; set; } = Guid.Parse("00000000-0000-0000-0000-0000000f0a01");

    /// <summary>Whether the caller holds <c>Grades.ChangeApprove</c>.</summary>
    public bool CanApprove { get; set; }

    /// <summary>Whether the gradebook is locked (a closed year); a change request outside a lock is refused.</summary>
    public bool Locked { get; set; } = true;

    public decimal? CurrentValue { get; set; } = 50m;

    public Task<GradeChangeRequestDto> CreateAsync(Guid classCourseId, CreateGradeChangeRequest request, CancellationToken ct = default)
    {
        if (!Locked) throw FakeProblems.Of(403, ProblemCodes.grade_change_not_allowed);
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length is < GradeChangeLimits.ReasonMinLength or > GradeChangeLimits.ReasonMaxLength)
            throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(CreateGradeChangeRequest.Reason));
        if (_requests.Any(r => r.State == GradeChangeStates.Pending && r.GradeItemId == request.GradeItemId && r.StudentId == request.StudentId))
            throw FakeProblems.Of(409, ProblemCodes.grade_change_pending);
        var now = Now();
        var dto = new GradeChangeRequestDto(Guid.NewGuid(), classCourseId, "الرياضيات", "Mathematics", request.GradeItemId, request.StudentId, "طالب", "Student",
            CurrentValue, request.NewValue, reason, GradeChangeStates.Pending, Me, "معلم", "Teacher", now, null, null, null, now);
        _requests.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<PagedResult<GradeChangeRequestDto>> ListAsync(GradeChangeQuery query, CancellationToken ct = default)
    {
        var rows = _requests.Where(r => (CanApprove || r.RequestedByUserId == Me)
            && (query.State is null || r.State == query.State)
            && (query.ClassCourseId is null || r.ClassCourseId == query.ClassCourseId))
            .OrderByDescending(r => r.RequestedAtUtc).Select(r => r with { ServerNowUtc = Now() });
        return Task.FromResult(FakeProblems.Page(rows, query.Page, query.PageSize));
    }

    public Task<GradeChangeRequestDto> ApproveAsync(Guid id, DecideGradeChangeRequest request, CancellationToken ct = default) => Decide(id, request, true);

    public Task<GradeChangeRequestDto> RejectAsync(Guid id, DecideGradeChangeRequest request, CancellationToken ct = default) => Decide(id, request, false);

    private Task<GradeChangeRequestDto> Decide(Guid id, DecideGradeChangeRequest request, bool approve)
    {
        if (!CanApprove) throw FakeProblems.Of(403, null);
        var i = _requests.FindIndex(r => r.Id == id);
        if (i < 0) throw FakeProblems.Of(404, ProblemCodes.not_found);
        var current = _requests[i];
        if (current.RequestedByUserId == Me) throw FakeProblems.Of(403, ProblemCodes.grade_change_not_allowed);
        if (current.State != GradeChangeStates.Pending) throw FakeProblems.Of(409, ProblemCodes.concurrency_conflict);
        var note = request.Note?.Trim();
        if (!approve && string.IsNullOrEmpty(note)) throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(DecideGradeChangeRequest.Note));
        var now = Now();
        var decided = current with { State = approve ? GradeChangeStates.Approved : GradeChangeStates.Rejected, DecidedByUserId = Me, DecidedAtUtc = now, DecisionNote = note, ServerNowUtc = now };
        _requests[i] = decided;
        return Task.FromResult(decided);
    }
}
