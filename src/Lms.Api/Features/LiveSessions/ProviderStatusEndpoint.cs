using Lms.Api.Infrastructure;
using Lms.Infrastructure.Zoom;
using Lms.Shared;
using Lms.Shared.Authorization;

namespace Lms.Api.Features.LiveSessions;

/// <summary>
/// Zoom provider wiring and the provider-status endpoint (M4b Task 5). The status says only whether the platform Zoom app is
/// configured and healthy; it never reveals account ids, hosts or secrets.
/// </summary>
public sealed class ZoomProviderModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddZoom(configuration);

    public void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet(Routes.LiveSessionsProviderStatus, async (ZoomProviderStatus status, CancellationToken ct) =>
                Results.Ok(await status.GetAsync(ct)))
            .RequireAuthorization()
            .RequirePermission(Permissions.LiveSessions.Manage);
}
