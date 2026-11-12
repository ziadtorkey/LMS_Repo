using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;

namespace Lms.Api.Features.Blueprints;

/// <summary>Versioned course blueprints: drafts, publish and copy (FR-LP-01, FR-LP-02, FR-LP-04, FR-CRS-04, D46).</summary>
public sealed class BlueprintsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BlueprintReader>();
        services.AddScoped<BlueprintDraftHandlers>();
        services.AddScoped<PublishBlueprintHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.CourseBlueprintsPattern, (Guid id, BlueprintDraftHandlers h, CancellationToken ct) => h.ListAsync(id, ct))
            .RequirePermission(Permissions.Blueprints.View);
        app.MapGet(Routes.CourseBlueprintVersionPattern,
                (Guid id, int version, BlueprintDraftHandlers h, CancellationToken ct) => h.GetVersionAsync(id, version, ct))
            .RequirePermission(Permissions.Blueprints.View);
        app.MapPost(Routes.CourseBlueprintDraftPattern,
                (Guid id, HttpContext http, BlueprintDraftHandlers h, CancellationToken ct) => h.CreateDraftAsync(id, http, ct))
            .RequirePermission(Permissions.Blueprints.Edit);
        app.MapGet(Routes.CourseBlueprintDraftPattern,
                (Guid id, HttpContext http, BlueprintDraftHandlers h, CancellationToken ct) => h.GetDraftAsync(id, http, ct))
            .RequirePermission(Permissions.Blueprints.Edit);
        app.MapPut(Routes.CourseBlueprintDraftPattern,
                (Guid id, SaveBlueprintDraftRequest r, HttpContext http, BlueprintDraftHandlers h, CancellationToken ct) =>
                    h.SaveDraftAsync(id, r, http, ct))
            .RequirePermission(Permissions.Blueprints.Edit);
        app.MapDelete(Routes.CourseBlueprintDraftPattern,
                (Guid id, HttpContext http, BlueprintDraftHandlers h, CancellationToken ct) => h.DeleteDraftAsync(id, http, ct))
            .RequirePermission(Permissions.Blueprints.Edit);
        app.MapPost(Routes.CourseBlueprintCopyFromPattern,
                (Guid id, CopyBlueprintRequest r, HttpContext http, BlueprintDraftHandlers h, CancellationToken ct) =>
                    h.CopyFromAsync(id, r, http, ct))
            .RequirePermission(Permissions.Blueprints.Edit);
        app.MapPost(Routes.CourseBlueprintPublishPattern,
                (Guid id, PublishBlueprintRequest r, HttpContext http, PublishBlueprintHandler h, CancellationToken ct) =>
                    h.HandleAsync(id, r, http, ct))
            .RequirePermission(Permissions.Blueprints.Publish);
    }
}
