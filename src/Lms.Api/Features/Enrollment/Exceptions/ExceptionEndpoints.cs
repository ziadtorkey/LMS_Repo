using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;

namespace Lms.Api.Features.Enrollment.Exceptions;

/// <summary>Exceptional enrollment (M3 Task 11, FR-CRS-03, D40): request, list, approve, reject, cancel.</summary>
internal static class ExceptionEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.ClassCourseExceptionsPattern,
                (Guid id, RequestExceptionRequest? body, HttpContext http, ExceptionRequestHandler h, CancellationToken ct) => h.RequestAsync(id, body, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Enrollment.RequestException);
        app.MapPost(Routes.EnrollmentExceptionCancelPattern,
                (Guid id, HttpContext http, ExceptionRequestHandler h, CancellationToken ct) => h.CancelAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Enrollment.RequestException);
        app.MapGet(Routes.EnrollmentExceptions,
                ([AsParameters] ExceptionQuery query, HttpContext http, ExceptionDecisionHandler h, CancellationToken ct) => h.ListAsync(query, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Enrollment.ApproveException);
        app.MapGet(Routes.EnrollmentExceptionPattern,
                (Guid id, HttpContext http, ExceptionDecisionHandler h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Enrollment.ApproveException);
        app.MapPost(Routes.EnrollmentExceptionApprovePattern,
                (Guid id, DecideExceptionRequest? body, HttpContext http, ExceptionDecisionHandler h, CancellationToken ct) => h.ApproveAsync(id, body, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Enrollment.ApproveException);
        app.MapPost(Routes.EnrollmentExceptionRejectPattern,
                (Guid id, DecideExceptionRequest? body, HttpContext http, ExceptionDecisionHandler h, CancellationToken ct) => h.RejectAsync(id, body, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Enrollment.ApproveException);
    }
}
