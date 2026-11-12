using Lms.Shared;
using Lms.Shared.Content;

namespace Lms.Client.Services.Api;

/// <summary>Completion and progress of the modules of a class course (FR-CNT-04).</summary>
public interface IProgressApi
{
    /// <summary>The caller's own progress, or (staff) that of an enrolled <paramref name="studentId"/>.</summary>
    Task<CourseProgressDto> GetCourseProgressAsync(Guid classCourseId, Guid? studentId = null, CancellationToken ct = default);

    Task<ModuleCompletionDto> GetCompletionAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>Marks a Manual-rule module complete (idempotent; 409 completion_not_manual for other rules).</summary>
    Task<ModuleCompletionDto> MarkCompleteAsync(Guid moduleId, CancellationToken ct = default);
}

public sealed class ProgressApi(ApiHttp http) : IProgressApi
{
    public Task<CourseProgressDto> GetCourseProgressAsync(Guid classCourseId, Guid? studentId = null, CancellationToken ct = default) =>
        http.GetAsync<CourseProgressDto>(studentId is { } s ? Routes.ClassCourseProgressOf(classCourseId, s) : Routes.ClassCourseProgress(classCourseId), ct);

    public Task<ModuleCompletionDto> GetCompletionAsync(Guid moduleId, CancellationToken ct = default) =>
        http.GetAsync<ModuleCompletionDto>(Routes.ModuleCompletion(moduleId), ct);

    public async Task<ModuleCompletionDto> MarkCompleteAsync(Guid moduleId, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleCompletionDto>(HttpMethod.Put, Routes.ModuleCompletion(moduleId), null, ct))!;
}

/// <summary>In-memory completion for the standalone dev server and tests: states by module id, Manual rule only.</summary>
public sealed class FakeProgressApi : IProgressApi
{
    public Dictionary<Guid, ModuleCompletionDto> Completions { get; } = [];

    public Task<CourseProgressDto> GetCourseProgressAsync(Guid classCourseId, Guid? studentId = null, CancellationToken ct = default)
    {
        var modules = Completions.Values.ToList();
        var done = modules.Count(m => m.State == CompletionStates.Completed);
        return Task.FromResult(new CourseProgressDto(classCourseId, studentId ?? Guid.Empty, new ProgressDto(done, modules.Count),
            [], modules, null, DateTime.UtcNow));
    }

    public Task<ModuleCompletionDto> GetCompletionAsync(Guid moduleId, CancellationToken ct = default) =>
        Completions.TryGetValue(moduleId, out var c)
            ? Task.FromResult(c)
            : Task.FromException<ModuleCompletionDto>(new ApiProblemException(404, "not_found", null, null));

    public Task<ModuleCompletionDto> MarkCompleteAsync(Guid moduleId, CancellationToken ct = default)
    {
        if (!Completions.TryGetValue(moduleId, out var c))
            return Task.FromException<ModuleCompletionDto>(new ApiProblemException(404, "not_found", null, null));
        if (c.Rule != CompletionRules.Manual)
            return Task.FromException<ModuleCompletionDto>(new ApiProblemException(409, ProblemCodes.completion_not_manual, null, null));
        return Task.FromResult(Completions[moduleId] = c with { State = CompletionStates.Completed, CompletedAtUtc = DateTime.UtcNow });
    }
}
