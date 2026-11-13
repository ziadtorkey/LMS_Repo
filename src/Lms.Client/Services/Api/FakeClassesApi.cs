using Lms.Shared;
using Lms.Shared.Classes;

namespace Lms.Client.Services.Api;

/// <summary>In-memory classes for the standalone client and bUnit tests; records what the UI sent.</summary>
public sealed class FakeClassesApi : IClassesApi
{
    private readonly List<ClassSummaryDto> _classes = [.. FakeClasses.All];

    public ClassQuery? LastQuery { get; private set; }
    public SaveClassRequest? LastSave { get; private set; }
    public Guid? LastUpdatedId { get; private set; }
    public BulkCreateClassesRequest? LastBulk { get; private set; }
    public CopyClassesRequest? LastCopy { get; private set; }
    public List<Guid> Closed { get; } = [];

    /// <summary>When set, the next write throws it (tests simulate server problems).</summary>
    public Exception? WriteFailure { get; set; }

    /// <summary>The codes the next copy reports as already existing in the target year.</summary>
    public List<string> CopySkipped { get; } = [];

    public IReadOnlyList<ClassSummaryDto> All => _classes;

    /// <summary>Forgets the last saved request (tests that seed data through the API call it before the step under test).</summary>
    public void ResetLastSave() => LastSave = null;

    public Task<PagedResult<ClassSummaryDto>> ListAsync(ClassQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _classes
            .Where(c => query.OrgUnitId is null || c.OrgUnitId == query.OrgUnitId)
            .Where(c => query.Status is null || c.Status == query.Status)
            .Where(c => string.IsNullOrWhiteSpace(query.Search)
                || c.Code.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || c.NameEn.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || c.NameAr.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(new PagedResult<ClassSummaryDto>(
            rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<ClassSummaryDto> GetAsync(Guid id, CancellationToken ct = default) =>
        _classes.FirstOrDefault(c => c.Id == id) is { } found
            ? Task.FromResult(found)
            : Task.FromException<ClassSummaryDto>(new ApiProblemException(404, "not_found", null, null));

    private ClassSummaryDto Make(SaveClassRequest r)
    {
        var unit = FakeUnits.All.FirstOrDefault(u => u.Id == r.OrgUnitId);
        return new ClassSummaryDto(Guid.NewGuid(), r.Code.ToUpperInvariant(), r.NameAr, r.NameEn, r.OrgUnitId, unit?.NameAr ?? "", unit?.NameEn ?? "",
            r.AcademicYearId, "", "", r.Capacity, 0, 0, r.HomeroomTeacherUserId, null, null, ClassStatuses.Active, false);
    }

    private bool CodeTaken(string code, Guid yearId, Guid? except) =>
        _classes.Any(c => c.Id != except && c.AcademicYearId == yearId && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public Task<ClassSummaryDto> CreateAsync(SaveClassRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is { } failure) return Task.FromException<ClassSummaryDto>(failure);
        LastSave = request;
        if (CodeTaken(request.Code, request.AcademicYearId, null))
            return Task.FromException<ClassSummaryDto>(new ApiProblemException(409, "class_code_taken", null, null, "code"));
        var created = Make(request);
        _classes.Add(created);
        return Task.FromResult(created);
    }

    public Task UpdateAsync(Guid id, SaveClassRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is { } failure) return Task.FromException(failure);
        LastSave = request;
        LastUpdatedId = id;
        var index = _classes.FindIndex(c => c.Id == id);
        if (index < 0) return Task.FromException(new ApiProblemException(404, "not_found", null, null));
        if (CodeTaken(request.Code, _classes[index].AcademicYearId, id))
            return Task.FromException(new ApiProblemException(409, "class_code_taken", null, null, "code"));
        _classes[index] = _classes[index] with
        {
            Code = request.Code.ToUpperInvariant(), NameAr = request.NameAr, NameEn = request.NameEn, Capacity = request.Capacity,
            HomeroomTeacherUserId = request.HomeroomTeacherUserId,
        };
        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid id, CancellationToken ct = default)
    {
        if (WriteFailure is { } failure) return Task.FromException(failure);
        Closed.Add(id);
        var index = _classes.FindIndex(c => c.Id == id);
        if (index >= 0) _classes[index] = _classes[index] with { Status = ClassStatuses.Closed, IsReadOnly = true };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClassSummaryDto>> BulkCreateAsync(BulkCreateClassesRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is { } failure) return Task.FromException<IReadOnlyList<ClassSummaryDto>>(failure);
        LastBulk = request;
        var made = Enumerable.Range(request.From, request.To - request.From + 1).Select(n => Make(new SaveClassRequest(
            request.CodePattern.Replace("{N}", n.ToString()), request.NamePatternAr.Replace("{N}", n.ToString()),
            request.NamePatternEn.Replace("{N}", n.ToString()), request.OrgUnitId, request.AcademicYearId, request.Capacity, null))).ToList();
        var taken = made.Where(c => CodeTaken(c.Code, request.AcademicYearId, null)).Select(c => c.Code).ToList();
        if (taken.Count > 0)
            return Task.FromException<IReadOnlyList<ClassSummaryDto>>(new ApiProblemException(409, "class_code_taken", null, null, codes: taken));
        _classes.AddRange(made);
        return Task.FromResult<IReadOnlyList<ClassSummaryDto>>(made);
    }

    public Task<CopyClassesResultDto> CopyAsync(CopyClassesRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is { } failure) return Task.FromException<CopyClassesResultDto>(failure);
        LastCopy = request;
        var sources = _classes.Where(c => c.OrgUnitId == request.OrgUnitId).ToList();
        return Task.FromResult(new CopyClassesResultDto(Math.Max(0, sources.Count - CopySkipped.Count), CopySkipped.ToList()));
    }
}
