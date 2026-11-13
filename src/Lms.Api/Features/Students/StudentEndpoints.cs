using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;

namespace Lms.Api.Features.Students;

public static class StudentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.Students, (CreateStudentRequest? request, HttpContext http, CreateStudentHandler handler, CancellationToken ct) =>
                handler.HandleAsync(request, http, ct))
            .RequirePermission(Permissions.Students.Create);
        app.MapGet(Routes.Students, ([AsParameters] StudentQuery query, HttpContext http, StudentQueries queries, CancellationToken ct) =>
                queries.ListAsync(query, http, ct))
            .RequirePermission(Permissions.Students.View);
        app.MapGet(Routes.StudentPattern, (Guid id, StudentQueries queries, CancellationToken ct) => queries.GetAsync(id, ct))
            .RequirePermission(Permissions.Students.View);
        app.MapPut(Routes.StudentPattern, (Guid id, UpdateStudentRequest? request, HttpContext http, StudentLifecycleHandler handler, CancellationToken ct) =>
                handler.UpdateAsync(id, request, http, ct))
            .RequirePermission(Permissions.Students.Update);
        app.MapPut(Routes.StudentStatusPattern, (Guid id, SetPersonStatusRequest? request, HttpContext http, StudentLifecycleHandler handler, CancellationToken ct) =>
                handler.SetStatusAsync(id, request, http, ct))
            .RequirePermission(Permissions.Students.Update);
        app.MapDelete(Routes.StudentPattern, (Guid id, HttpContext http, StudentLifecycleHandler handler, CancellationToken ct) =>
                handler.DeleteAsync(id, http, ct))
            .RequirePermission(Permissions.Students.Delete);
        app.MapPut(Routes.StudentFeatureFlagsPattern, (Guid id, SetStudentFeatureFlagsRequest? request, HttpContext http, StudentLifecycleHandler handler, CancellationToken ct) =>
                handler.SetFeatureFlagsAsync(id, request, http, ct))
            .RequirePermission(Permissions.Students.Update);
        app.MapPost(Routes.StudentResetPasswordPattern, (Guid id, ResetStudentPasswordRequest? request, HttpContext http, StudentLifecycleHandler handler, CancellationToken ct) =>
                handler.ResetPasswordAsync(id, request, http, ct))
            .RequirePermission(Permissions.Students.Update);
    }
}
