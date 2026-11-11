using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>Students (FR-STU-01..03). Update, status, delete and feature flags are appended by the profile task.</summary>
public interface IStudentsApi
{
    Task<PagedResult<StudentSummaryDto>> ListAsync(StudentQuery query, CancellationToken ct = default);
    Task<StudentDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates the student; the response is the only place the issued password is ever shown (D24).</summary>
    Task<StudentCreatedDto> CreateAsync(CreateStudentRequest request, CancellationToken ct = default);

    Task UpdateAsync(Guid id, UpdateStudentRequest request, CancellationToken ct = default);
    Task SetStatusAsync(Guid id, SetPersonStatusRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Flag name to value; null = inherit (section 4.4).</summary>
    Task SetFeatureFlagsAsync(Guid id, SetStudentFeatureFlagsRequest request, CancellationToken ct = default);

    /// <summary>Issues a new default password; like create, the response is the only place it is ever shown (D24).</summary>
    Task<StudentCreatedDto> ResetPasswordAsync(Guid id, ResetStudentPasswordRequest request, CancellationToken ct = default);
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

    public Task UpdateAsync(Guid id, UpdateStudentRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.Student(id), request, ct);

    public Task SetStatusAsync(Guid id, SetPersonStatusRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.StudentStatus(id), request, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Student(id), null, ct);

    public Task SetFeatureFlagsAsync(Guid id, SetStudentFeatureFlagsRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.StudentFeatureFlags(id), request, ct);

    public async Task<StudentCreatedDto> ResetPasswordAsync(Guid id, ResetStudentPasswordRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<StudentCreatedDto>(HttpMethod.Post, Routes.StudentResetPassword(id), request, ct))!;

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

    /// <summary>Links a new or registered guardian to the student (one primary per student).</summary>
    Task LinkAsync(Guid studentId, GuardianLinkInput input, CancellationToken ct = default);
    Task UpdateLinkAsync(Guid studentId, Guid guardianId, UpdateStudentGuardianRequest request, CancellationToken ct = default);

    /// <summary>Fails with guardian_required / primary_guardian_required when the unit needs the guardian or the primary must stay.</summary>
    Task UnlinkAsync(Guid studentId, Guid guardianId, CancellationToken ct = default);

    Task<PagedResult<GuardianSummaryDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<GuardianDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateGuardianRequest request, CancellationToken ct = default);
    Task SendPortalInvitationAsync(Guid id, CancellationToken ct = default);

    /// <summary>The signed-in guardian's children in the active context (one school or group).</summary>
    Task<IReadOnlyList<MyChildDto>> GetMyChildrenAsync(CancellationToken ct = default);
}

public sealed class GuardiansApi(ApiHttp http) : IGuardiansApi
{
    public Task LinkAsync(Guid studentId, GuardianLinkInput input, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StudentGuardians(studentId), input, ct);

    public Task UpdateLinkAsync(Guid studentId, Guid guardianId, UpdateStudentGuardianRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.StudentGuardian(studentId, guardianId), request, ct);

    public Task UnlinkAsync(Guid studentId, Guid guardianId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.StudentGuardian(studentId, guardianId), null, ct);

    public Task<PagedResult<GuardianSummaryDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<GuardianSummaryDto>>(
            $"{Routes.Guardians}?page={page}&pageSize={pageSize}" + (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}"), ct);

    public Task<GuardianDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<GuardianDetailDto>(Routes.Guardian(id), ct);

    public Task UpdateAsync(Guid id, UpdateGuardianRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.Guardian(id), request, ct);

    public Task SendPortalInvitationAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.GuardianPortalInvitation(id), null, ct);

    public async Task<IReadOnlyList<MyChildDto>> GetMyChildrenAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<MyChildDto>>(Routes.MyChildren, ct);

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

/// <summary>Classes as far as the student pages need them: the classes of a unit and year (the class tasks add the rest).</summary>
public interface IClassesApi
{
    Task<PagedResult<ClassSummaryDto>> ListAsync(ClassQuery query, CancellationToken ct = default);
}

public sealed class ClassesApi(ApiHttp http) : IClassesApi
{
    public Task<PagedResult<ClassSummaryDto>> ListAsync(ClassQuery query, CancellationToken ct = default)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}", $"includeDescendants={query.IncludeDescendants.ToString().ToLowerInvariant()}" };
        if (query.OrgUnitId is { } unit) parts.Add($"orgUnitId={unit}");
        if (query.AcademicYearId is { } year) parts.Add($"academicYearId={year}");
        if (!string.IsNullOrWhiteSpace(query.Status)) parts.Add($"status={Uri.EscapeDataString(query.Status)}");
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add($"search={Uri.EscapeDataString(query.Search)}");
        return http.GetAsync<PagedResult<ClassSummaryDto>>("/api/classes?" + string.Join('&', parts), ct);
    }
}
