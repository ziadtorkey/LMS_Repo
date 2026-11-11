using Lms.Shared;
using Lms.Shared.Courses;

namespace Lms.Client.Services.Api;

/// <summary>Course catalog (FR-CRS-01). Allocations, restrictions and supervisors arrive with the course profile (Task 30).</summary>
public interface ICoursesApi
{
    Task<PagedResult<CourseSummaryDto>> ListAsync(CourseQuery query, CancellationToken ct = default);
    Task<CourseDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CourseDetailDto> CreateAsync(SaveCourseRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken ct = default);

    /// <summary>Courses available at a unit (allocated there or above) and allocated below it.</summary>
    Task<UnitCoursesDto> GetUnitCoursesAsync(Guid orgUnitId, CancellationToken ct = default);
    Task AllocateAsync(AllocateCoursesRequest request, CancellationToken ct = default);
    Task RemoveAllocationAsync(Guid allocationId, CancellationToken ct = default);

    /// <summary>Replaces all restrictions; the result says how many active enrollments no longer pass (they are not dropped).</summary>
    Task<RestrictionsSavedResult> SetRestrictionsAsync(Guid id, SetCourseRestrictionsRequest request, CancellationToken ct = default);
    Task SetSupervisorsAsync(Guid id, SetCourseSupervisorsRequest request, CancellationToken ct = default);

    /// <summary>Courses the caller supervises (GET /api/me/supervised-courses).</summary>
    Task<IReadOnlyList<CourseSummaryDto>> GetMySupervisedAsync(CancellationToken ct = default);
}

/// <summary>Outcome of saving restrictions: <c>AffectedEnrollments</c> from the <c>X-Lms-Affected-Enrollments</c> header.</summary>
public sealed record RestrictionsSavedResult(int AffectedEnrollments);

public sealed class CoursesApi(ApiHttp http) : ICoursesApi
{
    private const string Route = "/api/courses";

    public Task<PagedResult<CourseSummaryDto>> ListAsync(CourseQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<CourseSummaryDto>>(Route + ToQueryString(query), ct);

    public Task<CourseDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<CourseDetailDto>($"{Route}/{id}", ct);

    public async Task<CourseDetailDto> CreateAsync(SaveCourseRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CourseDetailDto>(HttpMethod.Post, Route, request, ct))!;

    public Task UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, $"{Route}/{id}", request, ct);

    public async Task<UnitCoursesDto> GetUnitCoursesAsync(Guid orgUnitId, CancellationToken ct = default) =>
        await http.GetAsync<UnitCoursesDto>($"/api/org-units/{orgUnitId}/courses", ct);

    public Task AllocateAsync(AllocateCoursesRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, "/api/course-allocations", request, ct);

    public Task RemoveAllocationAsync(Guid allocationId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, $"/api/course-allocations/{allocationId}", null, ct);

    public async Task<RestrictionsSavedResult> SetRestrictionsAsync(Guid id, SetCourseRestrictionsRequest request, CancellationToken ct = default)
    {
        var (_, header) = await http.SendWithHeaderAsync<List<CourseRestrictionDto>>(HttpMethod.Put, $"{Route}/{id}/restrictions", request, "X-Lms-Affected-Enrollments", ct);
        return new RestrictionsSavedResult(int.TryParse(header, out var n) ? n : 0);
    }

    public Task SetSupervisorsAsync(Guid id, SetCourseSupervisorsRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, $"{Route}/{id}/supervisors", request, ct);

    public async Task<IReadOnlyList<CourseSummaryDto>> GetMySupervisedAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<CourseSummaryDto>>("/api/me/supervised-courses", ct);

    /// <summary>Query string of a course list request; unset filters are left out.</summary>
    public static string ToQueryString(CourseQuery q)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.OrgUnitId is { } unit)
        {
            parts.Add($"orgUnitId={unit}");
            parts.Add($"includeInherited={q.IncludeInherited.ToString().ToLowerInvariant()}");
        }
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        if (!string.IsNullOrWhiteSpace(q.Search)) parts.Add($"search={Uri.EscapeDataString(q.Search)}");
        if (q.SupervisedByMe) parts.Add("supervisedByMe=true");
        if (!string.IsNullOrWhiteSpace(q.Sort)) parts.Add($"sort={Uri.EscapeDataString(q.Sort)}");
        return "?" + string.Join('&', parts);
    }
}
