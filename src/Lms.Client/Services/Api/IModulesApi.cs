using Lms.Shared;
using Lms.Shared.Content;

namespace Lms.Client.Services.Api;

/// <summary>Lesson modules (FR-CNT). The tree of modules comes with the lesson plan; this reads one module with its content.</summary>
public interface IModulesApi
{
    /// <summary>GET /api/modules/{id}. A student read counts as a view (completes a View-rule module).</summary>
    Task<ModuleDetailDto> GetAsync(Guid moduleId, CancellationToken ct = default);
}

public sealed class ModulesApi(ApiHttp http) : IModulesApi
{
    public Task<ModuleDetailDto> GetAsync(Guid moduleId, CancellationToken ct = default) =>
        http.GetAsync<ModuleDetailDto>(Routes.Module(moduleId), ct);
}

/// <summary>In-memory modules for the standalone dev server and tests.</summary>
public sealed class FakeModulesApi : IModulesApi
{
    public Dictionary<Guid, ModuleDetailDto> Modules { get; } = [];

    public Task<ModuleDetailDto> GetAsync(Guid moduleId, CancellationToken ct = default) =>
        Modules.TryGetValue(moduleId, out var module)
            ? Task.FromResult(module)
            : Task.FromException<ModuleDetailDto>(new ApiProblemException(404, "not_found", null, null));
}
