using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>Student self-edit (<c>Students.SelfEdit</c>) and the change request queue (<c>Students.ApproveChanges</c>), D27.</summary>
internal static class SelfServiceEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MyStudentProfile, (MyStudentProfileHandler h, CancellationToken ct) => h.GetAsync(ct))
            .RequirePermission(Permissions.Students.SelfEdit);
        app.MapPut(Routes.MyStudentProfile, (UpdateMyStudentProfileRequest? request, MyStudentProfileHandler h, CancellationToken ct) =>
                h.UpdateAsync(request, ct))
            .RequirePermission(Permissions.Students.SelfEdit);
        app.MapPost(Routes.MyChangeRequests, (CreateChangeRequestRequest? request, MyChangeRequestsHandler h, CancellationToken ct) =>
                h.CreateAsync(request, ct))
            .RequirePermission(Permissions.Students.SelfEdit);
        app.MapGet(Routes.MyChangeRequests, (MyChangeRequestsHandler h, CancellationToken ct) => h.ListAsync(ct))
            .RequirePermission(Permissions.Students.SelfEdit);
        app.MapDelete(Routes.MyChangeRequestPattern, (Guid id, MyChangeRequestsHandler h, CancellationToken ct) => h.CancelAsync(id, ct))
            .RequirePermission(Permissions.Students.SelfEdit);

        app.MapGet(Routes.StudentChangeRequests,
                ([AsParameters] ChangeRequestQuery query, HttpContext http, ChangeRequestDecisionHandler h, CancellationToken ct) =>
                    h.ListAsync(query, http, ct))
            .RequirePermission(Permissions.Students.ApproveChanges);
        app.MapGet(Routes.StudentChangeRequestPattern, (Guid id, HttpContext http, ChangeRequestDecisionHandler h, CancellationToken ct) =>
                h.GetAsync(id, http, ct))
            .RequirePermission(Permissions.Students.ApproveChanges);
        app.MapPost(Routes.StudentChangeRequestApprovePattern,
                (Guid id, DecideChangeRequestRequest? body, HttpContext http, ChangeRequestDecisionHandler h, CancellationToken ct) =>
                    h.ApproveAsync(id, body, http, ct))
            .RequirePermission(Permissions.Students.ApproveChanges);
        app.MapPost(Routes.StudentChangeRequestRejectPattern,
                (Guid id, DecideChangeRequestRequest? body, HttpContext http, ChangeRequestDecisionHandler h, CancellationToken ct) =>
                    h.RejectAsync(id, body, http, ct))
            .RequirePermission(Permissions.Students.ApproveChanges);
    }
}
