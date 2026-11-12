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
}

public sealed class CoursesApi(ApiHttp http) : ICoursesApi
{
    public Task<PagedResult<CourseSummaryDto>> ListAsync(CourseQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<CourseSummaryDto>>(Routes.Courses + ToQueryString(query), ct);

    public Task<CourseDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<CourseDetailDto>(Routes.Course(id), ct);

    public async Task<CourseDetailDto> CreateAsync(SaveCourseRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CourseDetailDto>(HttpMethod.Post, Routes.Courses, request, ct))!;

    public Task UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.Course(id), request, ct);

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
