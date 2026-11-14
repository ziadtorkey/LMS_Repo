using Lms.Client.Components.Content;
using Lms.Shared;
using Lms.Shared.Enrollment;

namespace Lms.Client.Services.Api;

/// <summary>The student groups of a class course (FR-GRP-01), read-only here: module and activity targets pick from them.</summary>
public interface IClassCourseGroupsApi
{
    Task<IReadOnlyList<StudentGroupDto>> ListAsync(Guid classCourseId, CancellationToken ct = default);
}

public sealed class ClassCourseGroupsApi(ApiHttp http) : IClassCourseGroupsApi
{
    // An optional lookup: without Groups.View the target picker simply offers no groups.
    public Task<IReadOnlyList<StudentGroupDto>> ListAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetOptionalAsync<IReadOnlyList<StudentGroupDto>>(Routes.ClassCourseGroups(classCourseId), ct);
}

/// <summary>In-memory groups for the standalone dev server and tests.</summary>
public sealed class FakeClassCourseGroupsApi : IClassCourseGroupsApi
{
    public static readonly Guid GroupAId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    public static readonly Guid GroupBId = Guid.Parse("00000000-0000-0000-0000-0000000000e2");

    public List<StudentGroupDto> Groups { get; } =
    [
        new(GroupAId, FakeLessonPlansApi.ArabicClassCourseId, "المجموعة أ", "Group A", null, null, 4),
        new(GroupBId, FakeLessonPlansApi.ArabicClassCourseId, "المجموعة ب", "Group B", null, null, 3),
    ];

    public Task<IReadOnlyList<StudentGroupDto>> ListAsync(Guid classCourseId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StudentGroupDto>>(Groups.Where(g => g.ClassCourseId == classCourseId).ToList());
}

public static class ModuleGroups
{
    /// <summary>The groups of a class course as target options; a failed or forbidden lookup gives none (Group targeting just is not offered).</summary>
    public static async Task<IReadOnlyList<ModuleGroupOption>> LoadAsync(IClassCourseGroupsApi api, Guid classCourseId)
    {
        try
        {
            return (await api.ListAsync(classCourseId)).Select(g => new ModuleGroupOption(g.Id, g.NameAr, g.NameEn)).ToList();
        }
        catch (Exception ex) when (ex is ApiForbiddenException or ApiProblemException or HttpRequestException)
        {
            return [];
        }
    }
}
