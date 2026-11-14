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
        // No provider until the Zoom provider (Task 5) is registered when Zoom is configured: a create answers 409
        // provider_not_configured. The integration test host registers the fake provider instead.
        services.AddSingleton<IMeetingProvider, NotConfiguredMeetingProvider>();
        services.AddSingleton<ILiveSessionAttendanceSink, NoOpAttendanceSink>();
        services.AddScoped<LiveSessionService>();
        services.AddScoped<LiveSessionHandlers>();
        services.AddScoped<LiveSessionJoinHandlers>();
        services.AddScoped<LiveSessionReadHandlers>();
        services.AddScoped<LiveSessionLifecycle>();
        services.AddScoped<LiveReminderJob>();
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
        app.MapPost(Routes.LiveSessionJoinPattern, (Guid id, HttpContext http, LiveSessionJoinHandlers h, CancellationToken ct) => h.JoinAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.View);
        app.MapPut(Routes.LiveSessionRecordingPattern,
                (Guid id, RecordingLinkRequest? r, HttpContext http, LiveSessionJoinHandlers h, CancellationToken ct) => h.SetRecordingAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.Manage);
        app.MapGet(Routes.LiveSessionParticipantsPattern,
                (Guid id, [AsParameters] LiveParticipantQuery query, HttpContext http, LiveSessionReadHandlers h, CancellationToken ct) =>
                    h.ParticipantsAsync(id, query, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.Manage);
        app.MapGet(Routes.LiveSessionsUpcoming, (LiveSessionReadHandlers h, CancellationToken ct) => h.UpcomingAsync(ct))
            .RequireAuthorization().RequirePermission(Permissions.LiveSessions.View);
    }
}
