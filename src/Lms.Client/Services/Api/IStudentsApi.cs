using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>Students (FR-STU-01..03). Update, status, delete and feature flags are appended by the profile task.</summary>
public interface IStudentsApi
{
    Task<PagedResult<StudentSummaryDto>> ListAsync(StudentQuery query, CancellationToken ct = default);
    Task<StudentDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates the student; the response is the only place the issued password is ever shown (D24).</summary>
    Task<StudentCreatedDto> CreateAsync(CreateStudentRequest request, CancellationToken ct = default);
}

public sealed class StudentsApi(ApiHttp http) : IStudentsApi
{
    private const string Route = "/api/students";

    public Task<PagedResult<StudentSummaryDto>> ListAsync(StudentQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<StudentSummaryDto>>(Route + ToQueryString(query), ct);

    public Task<StudentDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<StudentDetailDto>($"{Route}/{id}", ct);

    public async Task<StudentCreatedDto> CreateAsync(CreateStudentRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<StudentCreatedDto>(HttpMethod.Post, Route, request, ct))!;

    /// <summary>Query string of a student list request; unset filters are left out.</summary>
    public static string ToQueryString(StudentQuery q)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.OrgUnitId is { } unit)
        {
            parts.Add($"orgUnitId={unit}");
            parts.Add($"includeDescendants={q.IncludeDescendants.ToString().ToLowerInvariant()}");
        }
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        if (!string.IsNullOrWhiteSpace(q.Gender)) parts.Add($"gender={Uri.EscapeDataString(q.Gender)}");
        if (!string.IsNullOrWhiteSpace(q.Search)) parts.Add($"search={Uri.EscapeDataString(q.Search)}");
        if (q.ClassId is { } cls) parts.Add($"classId={cls}");
        if (q.Unplaced) parts.Add("unplaced=true");
        if (q.AcademicYearId is { } year) parts.Add($"academicYearId={year}");
        if (!string.IsNullOrWhiteSpace(q.Sort)) parts.Add($"sort={Uri.EscapeDataString(q.Sort)}");
        return "?" + string.Join('&', parts);
    }
}

/// <summary>Guardian lookups used while creating a student (FR-STU-06).</summary>
public interface IGuardiansApi
{
    /// <summary>The registered guardian with this national id or mobile, or null. Reveals only the name and a masked mobile.</summary>
    Task<GuardianMatchDto?> MatchAsync(string? nationalId, string? mobile, CancellationToken ct = default);
}

public sealed class GuardiansApi(ApiHttp http) : IGuardiansApi
{
    public async Task<GuardianMatchDto?> MatchAsync(string? nationalId, string? mobile, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(nationalId)) parts.Add($"nationalId={Uri.EscapeDataString(nationalId)}");
        if (!string.IsNullOrWhiteSpace(mobile)) parts.Add($"mobile={Uri.EscapeDataString(mobile)}");
        if (parts.Count == 0) return null;
        try
        {
            return await http.GetAsync<GuardianMatchDto?>("/api/guardians/match?" + string.Join('&', parts), ct);
        }
        catch (ApiProblemException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
