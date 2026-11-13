using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>The approval queue of student change requests (FR-STU-04).</summary>
public interface IChangeRequestsApi
{
    Task<PagedResult<StudentChangeRequestDto>> ListAsync(ChangeRequestQuery query, CancellationToken ct = default);

    /// <summary>Applies the request; fails with change_request_closed or change_request_stale.</summary>
    Task ApproveAsync(Guid id, DecideChangeRequestRequest request, CancellationToken ct = default);

    /// <summary>A comment is required.</summary>
    Task RejectAsync(Guid id, DecideChangeRequestRequest request, CancellationToken ct = default);
}

public sealed class ChangeRequestsApi(ApiHttp http) : IChangeRequestsApi
{
    public Task<PagedResult<StudentChangeRequestDto>> ListAsync(ChangeRequestQuery q, CancellationToken ct = default)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.OrgUnitId is { } unit) parts.Add($"orgUnitId={unit}");
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        return http.GetAsync<PagedResult<StudentChangeRequestDto>>($"{Routes.StudentChangeRequests}?{string.Join('&', parts)}", ct);
    }

    public Task ApproveAsync(Guid id, DecideChangeRequestRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(id), request, ct);

    public Task RejectAsync(Guid id, DecideChangeRequestRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestReject(id), request, ct);
}

/// <summary>In-memory fake for the standalone dev server and bUnit tests; records what the UI sent.</summary>
public sealed class FakeChangeRequestsApi : IChangeRequestsApi
{
    private readonly List<StudentChangeRequestDto> _requests;

    public ChangeRequestQuery? LastQuery { get; private set; }
    public List<(Guid Id, string? Comment)> Approved { get; } = [];
    public List<(Guid Id, string? Comment)> Rejected { get; } = [];

    /// <summary>When set, the next decision throws it (tests simulate stale or closed requests).</summary>
    public Exception? Failure { get; set; }

    public FakeChangeRequestsApi()
    {
        StudentChangeRequestDto Make(string nameAr, string nameEn, string code, string status, params FieldChangeDto[] changes) =>
            new(Guid.NewGuid(), Guid.NewGuid(), code, nameAr, nameEn, "المدرسة الرئيسية", "Main School", status, changes, null,
                DateTime.UtcNow.AddHours(-2), null, null, null, null);
        _requests =
        [
            Make("أحمد سعيد", "Ahmed Saeed", "S-1001", ChangeRequestStatuses.Pending,
                new FieldChangeDto(StudentFields.NameEn, "Ahmed Saeed", "Ahmed Said"), new FieldChangeDto(StudentFields.DateOfBirth, "2014-03-05", "2014-03-06")),
            Make("ليلى حسن", "Laila Hassan", "S-1002", ChangeRequestStatuses.Pending, new FieldChangeDto(StudentFields.Nationality, "EG", "SA")),
            Make("عمر خالد", "Omar Khaled", "S-1003", ChangeRequestStatuses.Approved, new FieldChangeDto(StudentFields.Gender, "Male", "Male")),
        ];
    }

    public IReadOnlyList<StudentChangeRequestDto> All => _requests;

    public Task<PagedResult<StudentChangeRequestDto>> ListAsync(ChangeRequestQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _requests.Where(r => query.Status is null || r.Status == query.Status).ToList();
        return Task.FromResult(new PagedResult<StudentChangeRequestDto>(
            rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    private Task Decide(Guid id, string status, string? comment, List<(Guid, string?)> log)
    {
        if (Failure is { } failure)
        {
            Failure = null;
            return Task.FromException(failure);
        }
        log.Add((id, comment));
        var index = _requests.FindIndex(r => r.Id == id);
        _requests[index] = _requests[index] with { Status = status, DecisionComment = comment, DecidedAt = DateTime.UtcNow };
        return Task.CompletedTask;
    }

    public Task ApproveAsync(Guid id, DecideChangeRequestRequest request, CancellationToken ct = default) =>
        Decide(id, ChangeRequestStatuses.Approved, request.Comment, Approved);

    public Task RejectAsync(Guid id, DecideChangeRequestRequest request, CancellationToken ct = default) =>
        Decide(id, ChangeRequestStatuses.Rejected, request.Comment, Rejected);
}
