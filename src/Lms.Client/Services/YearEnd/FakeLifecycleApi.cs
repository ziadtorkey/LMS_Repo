using Lms.Shared;
using Lms.Shared.YearEnd;

namespace Lms.Client.Services.YearEnd;

/// <summary>
/// In-memory <see cref="ILifecycleApi"/>: a withdrawal needs an active student (409 <c>student_already_inactive</c>), a reason from the client list or "Other" with a note (400), and an
/// effective date within a year (422 <c>effective_date_invalid</c>); the student then leaves the active set, cannot sign in (<see cref="CanSignIn"/>) and appears in the archive with the reason
/// (staff only, D81). Re-admission needs a class with room (409 <c>readmit_class_full</c>) in an open year (409 <c>readmit_year_closed</c>) and keeps the student code (D25).
/// </summary>
public sealed class FakeLifecycleApi : ILifecycleApi
{
    public static readonly Guid ActiveStudentId = Guid.Parse("00000000-0000-0000-0000-0000000f0b01");
    public static readonly Guid OpenClassId = Guid.Parse("00000000-0000-0000-0000-0000000f0c01");
    public static readonly Guid FullClassId = Guid.Parse("00000000-0000-0000-0000-0000000f0c02");
    public static readonly Guid ClosedYearClassId = Guid.Parse("00000000-0000-0000-0000-0000000f0c03");

    private sealed record Person(Guid Id, string Code, string NameAr, string NameEn);

    private readonly Dictionary<Guid, Person> _people = [];
    private readonly HashSet<Guid> _active = [];
    private readonly List<ArchivedStudentDto> _archive = [];
    private List<WithdrawalReasonDto> _reasons =
    [
        new("MovedAway", "الانتقال إلى مدينة أخرى", "Moved away", true),
        new("Transferred", "الانتقال إلى مدرسة أخرى", "Transferred to another school", true),
    ];

    public FakeLifecycleApi()
    {
        Add(ActiveStudentId, "S00001", "سارة", "Sara");
        for (var i = 2; i <= 7; i++) Add(Guid.NewGuid(), $"S{i:D5}", $"طالب {i}", $"Student {i}");
    }

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <summary>The built-in role of the caller (D82): the fake holds what that role holds, as the server does. Org Admin by default.</summary>
    public string Role { get; set; } = Lms.Shared.Authorization.BuiltInRoles.OrgAdmin;

    private void Require(string permission)
    {
        if (!Lms.Shared.Authorization.BuiltInRoles.Get(Role).DefaultPermissions.Contains(permission)) throw FakeProblems.Of(403, null);
    }

    public IEnumerable<Guid> ActiveStudents => _active;

    public bool IsActive(Guid studentId) => _active.Contains(studentId);

    /// <summary>A withdrawn or graduated student cannot sign in from the effective date (FR-SEC-07).</summary>
    public bool CanSignIn(Guid studentId) => _active.Contains(studentId);

    public string? CodeOf(Guid studentId) => _people.TryGetValue(studentId, out var p) ? p.Code : null;

    private void Add(Guid id, string code, string ar, string en)
    {
        _people[id] = new Person(id, code, ar, en);
        _active.Add(id);
    }

    public Task WithdrawAsync(Guid studentId, WithdrawStudentRequest request, CancellationToken ct = default)
    {
        Require(Lms.Shared.Authorization.Permissions.Students.Withdraw);
        var reason = request.ReasonKey;
        if (reason != WithdrawalReasons.Other && !_reasons.Any(r => r.Key == reason && r.IsActive))
            throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(WithdrawStudentRequest.ReasonKey));
        if (reason == WithdrawalReasons.Other && string.IsNullOrWhiteSpace(request.Note))
            throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(WithdrawStudentRequest.Note));
        if ((request.Note?.Length ?? 0) > LifecycleLimits.NoteMaxLength) throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(WithdrawStudentRequest.Note));
        return Leave(studentId, StudentExitKinds.Withdrawn, reason, request.EffectiveDate);
    }

    public Task GraduateAsync(Guid studentId, GraduateStudentRequest request, CancellationToken ct = default)
    {
        Require(Lms.Shared.Authorization.Permissions.Students.Withdraw);
        return Leave(studentId, StudentExitKinds.Graduated, null, request.EffectiveDate);
    }

    private Task Leave(Guid studentId, string kind, string? reason, DateOnly effective)
    {
        var person = _people.GetValueOrDefault(studentId) ?? throw FakeProblems.Of(404, ProblemCodes.not_found);
        if (!_active.Contains(studentId)) throw FakeProblems.Of(409, ProblemCodes.student_already_inactive);
        var today = DateOnly.FromDateTime(Now());
        if (effective > today.AddYears(1) || effective < today.AddYears(-1)) throw FakeProblems.Of(422, ProblemCodes.effective_date_invalid);
        _active.Remove(studentId);
        _archive.Add(new ArchivedStudentDto(studentId, person.Code, person.NameAr, person.NameEn, kind, reason, effective, OpenClassId, "الفصل ١", "Class 1", "٢٠٢٥/٢٠٢٦", "2025/2026", true));
        return Task.CompletedTask;
    }

    public Task ReadmitAsync(Guid studentId, ReadmitStudentRequest request, CancellationToken ct = default)
    {
        Require(Lms.Shared.Authorization.Permissions.Students.Readmit);
        var entry = _archive.FirstOrDefault(a => a.StudentId == studentId) ?? throw FakeProblems.Of(404, ProblemCodes.not_found);
        if (request.ClassId == ClosedYearClassId) throw FakeProblems.Of(409, ProblemCodes.readmit_year_closed);
        if (request.ClassId == FullClassId) throw FakeProblems.Of(409, ProblemCodes.readmit_class_full);
        _archive.Remove(entry);
        _active.Add(studentId); // the code stays the same (D25)
        return Task.CompletedTask;
    }

    public Task<PagedResult<ArchivedStudentDto>> GetArchiveAsync(ArchiveQuery query, CancellationToken ct = default)
    {
        Require(Lms.Shared.Authorization.Permissions.Students.ViewArchive);
        var rows = _archive.Where(a => (query.ExitKind is null || a.ExitKind == query.ExitKind)
            && (string.IsNullOrWhiteSpace(query.Search) || a.NameEn.Contains(query.Search, StringComparison.OrdinalIgnoreCase) || a.StudentCode.Contains(query.Search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.EffectiveDate);
        return Task.FromResult(FakeProblems.Page(rows, query.Page, query.PageSize));
    }

    public Task<IReadOnlyList<WithdrawalReasonDto>> GetReasonsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WithdrawalReasonDto>>([.. _reasons]);

    public Task SaveReasonsAsync(SaveWithdrawalReasonsRequest request, CancellationToken ct = default)
    {
        if (request.Reasons.Any(r => r.Key == WithdrawalReasons.Other || string.IsNullOrWhiteSpace(r.Key)) || request.Reasons.Select(r => r.Key).Distinct().Count() != request.Reasons.Count)
            throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(SaveWithdrawalReasonsRequest.Reasons)); // "Other" is always offered and is not part of the list
        _reasons = [.. request.Reasons];
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory <see cref="INewYearApi"/>: nothing to copy or no template is 400 <c>copy_nothing_selected</c>, dates must be ordered (400) and must not overlap another year (409 <c>year_overlap</c>);
/// the preview lists what would be created and what is skipped; the copy is a job that ends with a new Planned year (<see cref="FakeNewYearApi.Slow"/> keeps it running until
/// <see cref="FakeNewYearApi.Advance"/>). Students are never copied.
/// </summary>
public sealed class FakeNewYearApi : INewYearApi
{
    private readonly Dictionary<Guid, NewYearRunDto> _runs = [];

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <summary>Date ranges of the years that already exist (an overlap is refused).</summary>
    public List<(DateOnly Start, DateOnly End)> ExistingYears { get; } = [(new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30))];

    public bool Slow { get; set; }

    private OpenNextYearPreviewDto Preview(OpenNextYearRequest r)
    {
        if (r.CopyFromYearId is null && r.TemplateId is null || r.CopyFromYearId is not null && r.CopyParts.Count == 0 && r.TemplateId is null)
            throw FakeProblems.Of(400, ProblemCodes.copy_nothing_selected);
        if (r.StartDate >= r.EndDate || string.IsNullOrWhiteSpace(r.NameEn) || string.IsNullOrWhiteSpace(r.NameAr))
            throw FakeProblems.Of(400, ProblemCodes.validation_failed, r.StartDate >= r.EndDate ? nameof(OpenNextYearRequest.EndDate) : nameof(OpenNextYearRequest.NameEn));
        if (ExistingYears.Any(y => r.StartDate <= y.End && y.Start <= r.EndDate)) throw FakeProblems.Of(409, ProblemCodes.year_overlap);
        var copy = r.CopyFromYearId is not null;
        var skipped = new List<SkippedItemDto>();
        if (r.TemplateId is not null) skipped.Add(new SkippedItemDto("Setting", "Locked", 2));
        return new OpenNextYearPreviewDto(
            copy && r.CopyParts.Contains(YearCopyParts.Classes) ? 8 : 0,
            copy && r.CopyParts.Contains(YearCopyParts.ClassCourses) ? 40 : 0,
            copy && r.CopyParts.Contains(YearCopyParts.TimetablePeriods) ? 7 : 0,
            r.TemplateId is not null ? 12 : 0, skipped, []);
    }

    public Task<OpenNextYearPreviewDto> PreviewAsync(Guid fromYearId, OpenNextYearRequest request, CancellationToken ct = default) =>
        Task.FromResult(Preview(request));

    public Task<NewYearRunDto> StartAsync(Guid fromYearId, OpenNextYearRequest request, CancellationToken ct = default)
    {
        var preview = Preview(request);
        var total = preview.Classes + preview.ClassCourses + preview.Periods + preview.UnitsFromTemplate;
        var run = new NewYearRunDto(Guid.NewGuid(), null, NewYearRunStates.Running, total, 0, preview, Now());
        _runs[run.Id] = run;
        if (!Slow) Advance(run.Id, total);
        return Task.FromResult(_runs[run.Id]);
    }

    /// <summary>Processes <paramref name="count"/> more items; the last one creates the Planned year and ends the job.</summary>
    public void Advance(Guid runId, int count)
    {
        var run = _runs[runId];
        if (run.State != NewYearRunStates.Running) return;
        var processed = Math.Min(run.Total, run.Processed + count);
        _runs[runId] = processed >= run.Total
            ? run with { Processed = processed, State = NewYearRunStates.Done, NewYearId = Guid.NewGuid(), ServerNowUtc = Now() }
            : run with { Processed = processed, ServerNowUtc = Now() };
    }

    public Task<NewYearRunDto> GetRunAsync(Guid fromYearId, Guid runId, CancellationToken ct = default) =>
        _runs.TryGetValue(runId, out var run) ? Task.FromResult(run with { ServerNowUtc = Now() }) : throw FakeProblems.Of(404, ProblemCodes.not_found);
}
