using Lms.Shared;
using Lms.Shared.YearEnd;

namespace Lms.Client.Services.YearEnd;

/// <summary>
/// In-memory <see cref="IPromotionApi"/> mimicking the rules the UI relies on: the source year must be closed (409 <c>year_not_closed</c>, D79), one running run per source unit,
/// the suggestion comes from the level mapping only (D83: the last level graduates, the others promote), decisions are saved as a draft (at most 200 per call), a bulk change takes a
/// filter, execute needs the typed student count, blocking issues refuse it, a failure for one student does not stop the rest (D84), undo only inside the window and before any activity
/// in the new year (D12). Time is the injected clock; <see cref="Slow"/> keeps a run Running until <see cref="Advance"/> is called, as a real job would be.
/// </summary>
public sealed class FakePromotionApi : IPromotionApi
{
    public static readonly Guid SourceYearId = Guid.Parse("00000000-0000-0000-0000-0000000f0001");
    public static readonly Guid TargetYearId = Guid.Parse("00000000-0000-0000-0000-0000000f0002");
    public static readonly Guid UnitId = Guid.Parse("00000000-0000-0000-0000-0000000f0003");
    public static readonly Guid FinalClassId = Guid.Parse("00000000-0000-0000-0000-0000000f00c3");

    private static readonly Guid[] ClassIds =
    [
        Guid.Parse("00000000-0000-0000-0000-0000000f00c1"), Guid.Parse("00000000-0000-0000-0000-0000000f00c2"), FinalClassId,
    ];

    private sealed class Row(Guid id, int number, Guid classId)
    {
        public Guid Id { get; } = id;
        public string Code { get; } = $"S{number:D5}";
        public string NameAr { get; } = $"طالب {number}";
        public string NameEn { get; } = $"Student {number}";
        public Guid ClassId { get; } = classId;
        public string Suggested { get; } = classId == FinalClassId ? PromotionOutcomes.Graduate : PromotionOutcomes.Promote;
        public string Outcome { get; set; } = classId == FinalClassId ? PromotionOutcomes.Graduate : PromotionOutcomes.Promote;
        public Guid? TargetClassId { get; set; }
        public string? Failure { get; set; }
    }

    private sealed class Run
    {
        public required Guid Id { get; init; }
        public required CreatePromotionRunRequest Request { get; init; }
        public string State { get; set; } = PromotionRunStates.Draft;
        public int Processed { get; set; }
        public DateTime CreatedAt { get; init; }
        public DateTime? ExecutedAt { get; set; }
        public DateTime? UndoUntil { get; set; }
        public List<Row> Rows { get; init; } = [];
    }

    private readonly List<Run> _runs = [];

    public FakePromotionApi(int students = 30)
    {
        Students = students;
    }

    public int Students { get; }

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;
    public Guid Me { get; set; } = Guid.Parse("00000000-0000-0000-0000-0000000f0a02");

    public bool SourceClosed { get; set; } = true;
    public bool TargetClosed { get; set; }

    /// <summary>The level mapping is missing: a blocking issue (the preview links to the Level Transfer tab).</summary>
    public bool LevelMappingMissing { get; set; }

    /// <summary>Something happened in the new year after a promotion: the undo is refused (D12).</summary>
    public bool ActivityInNewYear { get; set; }

    public int UndoWindowMinutes { get; set; } = PromotionLimits.DefaultUndoWindowMinutes;

    /// <summary>The run stays Running after execute until <see cref="Advance"/> (a real job).</summary>
    public bool Slow { get; set; }

    /// <summary>Students (by row index, 1-based) whose placement fails on execute (D84).</summary>
    public HashSet<int> FailStudents { get; } = [];

    private Run Find(Guid id) => _runs.FirstOrDefault(r => r.Id == id) ?? throw FakeProblems.Of(404, ProblemCodes.not_found);

    public Task<PromotionRunDto> CreateRunAsync(CreatePromotionRunRequest request, CancellationToken ct = default)
    {
        if (!SourceClosed) throw FakeProblems.Of(409, ProblemCodes.year_not_closed);
        if (_runs.Any(r => r.Request.SourceOrgUnitId == request.SourceOrgUnitId && r.State == PromotionRunStates.Running))
            throw FakeProblems.Of(409, ProblemCodes.promotion_in_progress);
        var run = new Run { Id = Guid.NewGuid(), Request = request, CreatedAt = Now() };
        for (var i = 1; i <= Students; i++) run.Rows.Add(new Row(Guid.NewGuid(), i, ClassIds[(i - 1) % ClassIds.Length]));
        _runs.Add(run);
        return Task.FromResult(Dto(run));
    }

    public Task<PromotionRunDto> GetRunAsync(Guid runId, CancellationToken ct = default) => Task.FromResult(Dto(Find(runId)));

    public Task<PagedResult<PromotionRunDto>> ListRunsAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        Task.FromResult(FakeProblems.Page(_runs.OrderByDescending(r => r.CreatedAt).Select(Dto), page, pageSize));

    public Task<PagedResult<PromotionRowDto>> GetRowsAsync(Guid runId, PromotionRowsQuery query, CancellationToken ct = default)
    {
        var run = Find(runId);
        var rows = Filter(run, query).Select(r => RowDto(run, r));
        return Task.FromResult(FakeProblems.Page(rows, query.Page, query.PageSize));
    }

    public Task SaveDecisionsAsync(Guid runId, SavePromotionDecisionsRequest request, CancellationToken ct = default)
    {
        var run = Find(runId);
        if (run.State != PromotionRunStates.Draft) throw FakeProblems.Of(409, ProblemCodes.promotion_conflict);
        if (request.Decisions.Count > PromotionLimits.DecisionsPerCall || request.Decisions.Any(d => !PromotionOutcomes.All.Contains(d.Outcome)))
            throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(SavePromotionDecisionsRequest.Decisions));
        foreach (var d in request.Decisions)
        {
            var row = run.Rows.FirstOrDefault(r => r.Id == d.StudentId) ?? throw FakeProblems.Of(404, ProblemCodes.not_found);
            row.Outcome = d.Outcome;
            row.TargetClassId = d.TargetClassId;
        }

        return Task.CompletedTask;
    }

    public Task<PromotionCountsDto> SetOutcomeForFilterAsync(Guid runId, SetPromotionOutcomeForFilterRequest request, CancellationToken ct = default)
    {
        var run = Find(runId);
        if (run.State != PromotionRunStates.Draft) throw FakeProblems.Of(409, ProblemCodes.promotion_conflict);
        if (request.Outcome is not null && !PromotionOutcomes.All.Contains(request.Outcome)) throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(SetPromotionOutcomeForFilterRequest.Outcome));
        foreach (var row in Filter(run, request.Filter))
        {
            row.Outcome = request.Outcome ?? row.Suggested; // null resets the filtered rows to the suggestion
            row.TargetClassId = request.Outcome is null ? null : request.TargetClassId;
        }

        return Task.FromResult(Counts(run));
    }

    public Task<PromotionRunDto> ExecuteAsync(Guid runId, ExecutePromotionRequest request, CancellationToken ct = default)
    {
        var run = Find(runId);
        if (run.State != PromotionRunStates.Draft || Blocking(run).Count > 0) throw FakeProblems.Of(409, ProblemCodes.promotion_conflict);
        if (request.ConfirmStudentCount != run.Rows.Count) throw FakeProblems.Of(400, ProblemCodes.validation_failed, nameof(ExecutePromotionRequest.ConfirmStudentCount));
        run.ExecutedAt = Now();
        run.State = PromotionRunStates.Running;
        if (!Slow) Advance(run.Id, run.Rows.Count);
        return Task.FromResult(Dto(run));
    }

    /// <summary>Processes <paramref name="count"/> more students of a Running run; the last one ends it (D84: a failure for one student does not stop the rest).</summary>
    public void Advance(Guid runId, int count)
    {
        var run = Find(runId);
        if (run.State != PromotionRunStates.Running) return;
        for (var i = 0; i < count && run.Processed < run.Rows.Count; i++)
        {
            var index = run.Processed + 1;
            if (FailStudents.Contains(index)) run.Rows[index - 1].Failure = "PlacementFailed";
            run.Processed++;
        }

        if (run.Processed < run.Rows.Count) return;
        run.State = run.Rows.Any(r => r.Failure is not null) ? PromotionRunStates.PartiallyFailed : PromotionRunStates.Done;
        run.UndoUntil = run.ExecutedAt!.Value.AddMinutes(UndoWindowMinutes);
    }

    public Task<PromotionRunDto> UndoAsync(Guid runId, CancellationToken ct = default)
    {
        var run = Find(runId);
        if (run.State is not (PromotionRunStates.Done or PromotionRunStates.PartiallyFailed)) throw FakeProblems.Of(409, ProblemCodes.promotion_conflict);
        if (run.UndoUntil is not { } until || Now() > until) throw FakeProblems.Of(409, ProblemCodes.promotion_undo_window_over);
        if (ActivityInNewYear) throw FakeProblems.Of(409, ProblemCodes.promotion_undo_activity_exists);
        run.State = PromotionRunStates.Undone;
        run.UndoUntil = null;
        foreach (var row in run.Rows) row.Failure = null;
        return Task.FromResult(Dto(run));
    }

    public Task<PagedResult<PromotionFailureDto>> GetFailuresAsync(Guid runId, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default)
    {
        var run = Find(runId);
        var rows = run.Rows.Where(r => r.Failure is not null).Select(r => new PromotionFailureDto(r.Id, r.Code, r.NameAr, r.NameEn, r.Failure!));
        return Task.FromResult(FakeProblems.Page(rows, page, pageSize));
    }

    private static IEnumerable<Row> Filter(Run run, PromotionRowsQuery q) => run.Rows.Where(r =>
        (q.Outcome is null || r.Outcome == q.Outcome) && (q.ClassId is null || r.ClassId == q.ClassId)
        && (!q.ProblemsOnly || r.Failure is not null) // a problem row: one that failed on execute (the preview issues are run level here)
        && (string.IsNullOrWhiteSpace(q.Search) || r.NameEn.Contains(q.Search, StringComparison.OrdinalIgnoreCase) || r.Code.Contains(q.Search, StringComparison.OrdinalIgnoreCase)));

    private List<YearEndIssueDto> Blocking(Run run)
    {
        var list = new List<YearEndIssueDto>();
        if (LevelMappingMissing) list.Add(new(YearEndIssueCodes.NoLevelMapping, 1, null));
        if (TargetClosed) list.Add(new(YearEndIssueCodes.TargetYearClosed, 1, run.Request.TargetYearId));
        return list;
    }

    private static PromotionCountsDto Counts(Run run) => new(
        run.Rows.Count(r => r.Outcome == PromotionOutcomes.Promote), run.Rows.Count(r => r.Outcome == PromotionOutcomes.Repeat),
        run.Rows.Count(r => r.Outcome == PromotionOutcomes.Graduate), run.Rows.Count(r => r.Outcome == PromotionOutcomes.TransferOut), 0);

    private static PromotionRowDto RowDto(Run run, Row r)
    {
        var idx = Array.IndexOf(ClassIds, r.ClassId) + 1;
        return new PromotionRowDto(r.Id, r.Code, r.NameAr, r.NameEn, r.ClassId, $"الفصل {idx}", $"Class {idx}", r.Outcome, r.Suggested, r.TargetClassId, null, null, null, [], r.Failure);
    }

    private PromotionRunDto Dto(Run run) => new(run.Id, run.Request.SourceYearId, run.Request.SourceOrgUnitId, run.Request.TargetYearId, run.Request.IncludeSubUnits, run.State,
        run.Rows.Count, run.Processed, run.Rows.Count(r => r.Failure is not null), Counts(run), Blocking(run), [], run.UndoUntil, run.CreatedAt, run.ExecutedAt, Me, Now());
}
