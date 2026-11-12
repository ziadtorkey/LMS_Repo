using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;

namespace Lms.Api.Features.Classes;

public static class ClassCourseEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassesMatrix,
                (Guid orgUnitId, Guid yearId, HttpContext http, MatrixQuery q, CancellationToken ct) => q.GetAsync(orgUnitId, yearId, http, ct))
            .RequirePermission(Permissions.Classes.View);
        app.MapGet(Routes.ClassCoursesOfClassPattern,
                (Guid id, HttpContext http, ClassCourseHandlers h, CancellationToken ct) => h.ListAsync(id, http, ct))
            .RequirePermission(Permissions.Classes.View);
        app.MapPost(Routes.ClassCoursesOfClassPattern,
                (Guid id, AddClassCoursesRequest r, HttpContext http, ClassCourseHandlers h, CancellationToken ct) => h.AddAsync(id, r, http, ct))
            .RequirePermission(Permissions.Classes.ManageCourses);
        app.MapPut(Routes.ClassCoursePattern,
                (Guid id, UpdateClassCourseRequest r, HttpContext http, ClassCourseHandlers h, CancellationToken ct) => h.UpdateAsync(id, r, http, ct))
            .RequirePermission(Permissions.Classes.ManageCourses);
        app.MapDelete(Routes.ClassCoursePattern,
                (Guid id, HttpContext http, ClassCourseHandlers h, CancellationToken ct) => h.DeleteAsync(id, http, ct))
            .RequirePermission(Permissions.Classes.ManageCourses);
        app.MapPost(Routes.ClassCoursesBulkAssign,
                (BulkAssignCourseRequest r, HttpContext http, ClassCourseHandlers h, CancellationToken ct) => h.BulkAssignAsync(r, http, ct))
            .RequirePermission(Permissions.Classes.ManageCourses);
    }
}
