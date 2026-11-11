using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;

namespace Lms.Api.Features.Staff;

public static class StaffEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.Staff,
                (CreateStaffRequest request, HttpContext http, CreateStaffHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, http, ct))
            .RequirePermission(Permissions.Staff.Create);
        app.MapGet(Routes.Staff,
                ([AsParameters] StaffQuery query, HttpContext http, StaffQueries queries, CancellationToken ct) =>
                    queries.ListAsync(query, http, ct))
            .RequirePermission(Permissions.Staff.View);
        app.MapGet(Routes.StaffPattern,
                (Guid id, StaffQueries queries, CancellationToken ct) => queries.GetAsync(id, ct))
            .RequirePermission(Permissions.Staff.View);
        app.MapPut(Routes.StaffPattern,
                (Guid id, UpdateStaffRequest? request, HttpContext http, UpdateStaffHandler handler, CancellationToken ct) =>
                    handler.UpdateAsync(id, request, http, ct))
            .RequirePermission(Permissions.Staff.Update);
        app.MapPut(Routes.StaffStatusPattern,
                (Guid id, SetPersonStatusRequest? request, HttpContext http, StaffStatusHandler handler, CancellationToken ct) =>
                    handler.SetAsync(id, request, http, ct))
            .RequirePermission(Permissions.Staff.Update);
        app.MapDelete(Routes.StaffPattern,
                (Guid id, HttpContext http, DeleteStaffHandler handler, CancellationToken ct) => handler.DeleteAsync(id, http, ct))
            .RequirePermission(Permissions.Staff.Delete);
        app.MapPost(Routes.StaffResendInvitationPattern,
                (Guid id, HttpContext http, UpdateStaffHandler handler, CancellationToken ct) => handler.ResendInvitationAsync(id, http, ct))
            .RequirePermission(Permissions.Staff.Update);
        app.MapPost(Routes.StaffSharesPattern,
                (Guid id, ShareStaffRequest? request, HttpContext http, ShareStaffHandler handler, CancellationToken ct) =>
                    handler.ShareAsync(id, request, http, ct))
            .RequirePermission(Permissions.Assignments.Create);
        app.MapDelete(Routes.StaffSharePattern,
                (Guid id, Guid assignmentId, HttpContext http, ShareStaffHandler handler, CancellationToken ct) =>
                    handler.UnshareAsync(id, assignmentId, http, ct))
            .RequirePermission(Permissions.Assignments.Delete);
    }
}
