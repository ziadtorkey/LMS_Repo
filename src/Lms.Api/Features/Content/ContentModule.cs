using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;

namespace Lms.Api.Features.Content;

/// <summary>Lesson modules, completion and progress (M4a Tasks 3 and 6: FR-LP-05, FR-CNT-01, FR-CNT-02, FR-CNT-04, D48, D51).</summary>
public sealed class ContentModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ModuleAccess>();
        services.AddScoped<ModuleHandlers>();
        services.AddScoped<CompletionRecorder>();
        services.AddScoped<ProgressReader>();
        services.AddScoped<CompletionHandlers>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        AssetEndpoints.Map(app);
        app.MapGet(Routes.ClassCourseLessonModulesPattern,
                (Guid id, Guid lessonKey, HttpContext http, ModuleHandlers h, CancellationToken ct) => h.ListAsync(id, lessonKey, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.View);
        app.MapPost(Routes.ClassCourseLessonModulesPattern,
                (Guid id, Guid lessonKey, CreateModuleRequest? r, HttpContext http, ModuleHandlers h, CancellationToken ct) =>
                    h.CreateAsync(id, lessonKey, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.Manage);
        app.MapPut(Routes.ClassCourseLessonModulesOrderPattern,
                (Guid id, Guid lessonKey, ReorderModulesRequest? r, HttpContext http, ModuleHandlers h, CancellationToken ct) =>
                    h.ReorderAsync(id, lessonKey, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.Manage);
        app.MapGet(Routes.ModulePattern, (Guid id, HttpContext http, ModuleHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.View);
        app.MapPut(Routes.ModulePattern,
                (Guid id, UpdateModuleRequest? r, HttpContext http, ModuleHandlers h, CancellationToken ct) => h.UpdateAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.Manage);
        app.MapDelete(Routes.ModulePattern, (Guid id, HttpContext http, ModuleHandlers h, CancellationToken ct) => h.DeleteAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.Manage);
        app.MapPost(Routes.ModuleMovePattern,
                (Guid id, MoveModuleRequest? r, HttpContext http, ModuleHandlers h, CancellationToken ct) => h.MoveAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.Manage);
        app.MapGet(Routes.ModuleCompletionPattern,
                (Guid id, HttpContext http, CompletionHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.View);
        app.MapPut(Routes.ModuleCompletionPattern,
                (Guid id, HttpContext http, CompletionHandlers h, CancellationToken ct) => h.MarkAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.View);
        app.MapGet(Routes.ClassCourseProgressPattern,
                (Guid id, Guid? studentId, HttpContext http, CompletionHandlers h, CancellationToken ct) => h.ProgressAsync(id, studentId, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Modules.View);
    }
}
