using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Packages;

namespace Lms.Api.Features.Packages;

/// <summary>The routes of M4b Task 9b: launch, preview and my-state of a package, commit and finish of an attempt, the grading method (D57).</summary>
internal static class PackageAttemptEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.PackageLaunchPattern, (Guid id, HttpContext http, PackageAttemptHandlers h, CancellationToken ct) => h.LaunchAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Packages.View);
        app.MapPost(Routes.PackagePreviewPattern, (Guid id, HttpContext http, PackageAttemptHandlers h, CancellationToken ct) => h.PreviewAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Packages.View);
        app.MapGet(Routes.PackageMyStatePattern, (Guid id, HttpContext http, PackageAttemptHandlers h, CancellationToken ct) => h.MyStateAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Packages.View);
        app.MapPost(Routes.PackageAttemptCommitPattern,
                (Guid id, PackageCommitRequest? r, HttpContext http, PackageAttemptHandlers h, CancellationToken ct) => h.CommitAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Packages.View);
        app.MapPost(Routes.PackageAttemptFinishPattern,
                (Guid id, PackageFinishRequest? r, HttpContext http, PackageAttemptHandlers h, CancellationToken ct) => h.FinishAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Packages.View);
    }
}
