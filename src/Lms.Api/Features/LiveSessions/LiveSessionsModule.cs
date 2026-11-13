using Lms.Api.Infrastructure;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.LiveSessions;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.LiveSessions;

namespace Lms.Api.Features.LiveSessions;

/// <summary>Live sessions of a class course: authoring, series and reading (M4b Task 4: FR-VC-01, D49, D53).</summary>
public sealed class LiveSessionsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // The fake provider until the Zoom provider (Task 5) is registered when Zoom is configured.
        services.AddSingleton<FakeMeetingProvider>();
        services.AddSingleton<IMeetingProvider>(sp => sp.GetRequiredService<FakeMeetingProvider>());
        services.AddSingleton<ILiveSessionAttendanceSink, NoOpAttendanceSink>();
        services.AddScoped<LiveSessionService>();
        services.AddScoped<LiveSessionHandlers>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseLiveSessionsPattern,
                (Guid id, [AsParameters] LiveSessionQuery query, HttpContext http, LiveSessionHandlers h, CancellationToken ct) =>
                    h.ListAsync(id, query, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.View);
        app.MapPost(Routes.ClassCourseLiveSessionsPattern,
                (Guid id, CreateLiveSessionRequest? r, HttpContext http, LiveSessionHandlers h, CancellationToken ct) =>
                    h.CreateAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.Manage);
        app.MapGet(Routes.LiveSessionPattern, (Guid id, HttpContext http, LiveSessionHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.View);
        app.MapPut(Routes.LiveSessionPattern,
                (Guid id, UpdateLiveSessionRequest? r, HttpContext http, LiveSessionHandlers h, CancellationToken ct) =>
                    h.UpdateAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.Manage);
        app.MapDelete(Routes.LiveSessionPattern,
                (Guid id, string? scope, HttpContext http, LiveSessionHandlers h, CancellationToken ct) => h.DeleteAsync(id, scope, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.Manage);
    }
}
