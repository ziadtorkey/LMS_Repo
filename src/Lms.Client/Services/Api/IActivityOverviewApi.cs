using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Quizzes;

namespace Lms.Client.Services.Api;

/// <summary>
/// Read-only lists of a ClassCourse's assignments and quizzes, used by the home cards (due, open, to grade).
/// The assignment and quiz pages bring their own full clients.
/// </summary>
public interface IActivityOverviewApi
{
    /// <summary>GET /api/class-courses/{id}/assignments — staff all, students the visible ones with their state.</summary>
    Task<PagedResult<CourseAssignmentListItemDto>> ListAssignmentsAsync(Guid classCourseId, CancellationToken ct = default);

    /// <summary>GET /api/class-courses/{id}/quizzes.</summary>
    Task<PagedResult<QuizListItemDto>> ListQuizzesAsync(Guid classCourseId, CancellationToken ct = default);
}

public sealed class ActivityOverviewApi(ApiHttp http) : IActivityOverviewApi
{
    public Task<PagedResult<CourseAssignmentListItemDto>> ListAssignmentsAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<CourseAssignmentListItemDto>>($"{Routes.ClassCourseAssignments(classCourseId)}?pageSize={Paging.MaxPageSize}", ct);

    public Task<PagedResult<QuizListItemDto>> ListQuizzesAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<QuizListItemDto>>($"{Routes.ClassCourseQuizzes(classCourseId)}?pageSize={Paging.MaxPageSize}", ct);
}

/// <summary>In-memory lists per class course for the dev server and tests.</summary>
public sealed class FakeActivityOverviewApi : IActivityOverviewApi
{
    public Dictionary<Guid, List<CourseAssignmentListItemDto>> Assignments { get; } = [];
    public Dictionary<Guid, List<QuizListItemDto>> Quizzes { get; } = [];
    public Exception? Failure { get; set; }

    public Task<PagedResult<CourseAssignmentListItemDto>> ListAssignmentsAsync(Guid classCourseId, CancellationToken ct = default)
    {
        if (Failure is not null) return Task.FromException<PagedResult<CourseAssignmentListItemDto>>(Failure);
        var items = Assignments.GetValueOrDefault(classCourseId) ?? [];
        return Task.FromResult(new PagedResult<CourseAssignmentListItemDto>(items, items.Count, 1, Paging.MaxPageSize));
    }

    public Task<PagedResult<QuizListItemDto>> ListQuizzesAsync(Guid classCourseId, CancellationToken ct = default)
    {
        if (Failure is not null) return Task.FromException<PagedResult<QuizListItemDto>>(Failure);
        var items = Quizzes.GetValueOrDefault(classCourseId) ?? [];
        return Task.FromResult(new PagedResult<QuizListItemDto>(items, items.Count, 1, Paging.MaxPageSize));
    }
}
