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
    /// <summary>Dev server data: a to-do assignment, a draft, one with submissions to grade, and one open quiz.</summary>
    public FakeActivityOverviewApi WithDemoData()
    {
        var cc = FakeLessonPlansApi.ArabicClassCourseId;
        var now = DateTime.UtcNow;
        CourseAssignmentListItemDto A(string ar, string en, DateTime? due, string? mine, int? submitted = null, int? graded = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ar, en, "Published", true, due, 20, submitted, graded, mine, null);
        Assignments[cc] =
        [
            A("واجب القراءة", "Reading homework", now.AddDays(2), null, 12, 5),
            A("مسودة التعبير", "Essay draft", now.AddDays(5), "Draft", 8, 8),
            A("واجب مسلَّم", "Handed in", now.AddDays(1), "Submitted", 3, 3),
        ];
        Quizzes[cc] = [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "اختبار الحروف", "Letters quiz", "Published", true, now.AddHours(-2), now.AddDays(1), 5, null, 0, null)];
        return this;
    }

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
