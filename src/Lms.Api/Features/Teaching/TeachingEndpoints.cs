using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Teaching;

namespace Lms.Api.Features.Teaching;

public static class TeachingEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.TeacherEligibility,
                (GrantEligibilityRequest r, HttpContext http, EligibilityHandlers h, CancellationToken ct) => h.GrantAsync(r, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.Manage);
        app.MapGet(Routes.TeacherEligibility,
                ([AsParameters] EligibilityQuery q, HttpContext http, EligibilityHandlers h, CancellationToken ct) => h.ListAsync(q, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.View);
        app.MapDelete(Routes.TeacherEligibilityItemPattern,
                (Guid id, HttpContext http, EligibilityHandlers h, CancellationToken ct) => h.DeleteAsync(id, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.Manage);

        app.MapGet(Routes.ClassCourseTeachersPattern,
                (Guid id, bool? includeHistory, HttpContext http, TeacherAssignmentHandlers h, CancellationToken ct) =>
                    h.ListAsync(id, includeHistory ?? false, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.View);
        app.MapPost(Routes.ClassCourseTeachersPattern,
                (Guid id, AssignTeacherRequest r, HttpContext http, TeacherAssignmentHandlers h, CancellationToken ct) => h.AssignAsync(id, r, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.Manage);
        app.MapPost(Routes.ClassCourseTeacherReplacePattern,
                (Guid id, Guid assignmentId, ReplaceTeacherRequest r, HttpContext http, TeacherAssignmentHandlers h, CancellationToken ct) =>
                    h.ReplaceAsync(id, assignmentId, r, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.Manage);
        app.MapPost(Routes.ClassCourseTeacherEndPattern,
                (Guid id, Guid assignmentId, EndTeacherRequest r, HttpContext http, TeacherAssignmentHandlers h, CancellationToken ct) =>
                    h.EndAsync(id, assignmentId, r, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.Manage);
        app.MapPost(Routes.TeacherAssignmentsBulk,
                (BulkAssignTeacherRequest r, HttpContext http, TeacherAssignmentHandlers h, CancellationToken ct) => h.BulkAsync(r, http, ct))
            .RequirePermission(Permissions.TeacherAssignments.Manage);
        app.MapGet(Routes.MyClassCourses,
                (Guid? yearId, MyClassCoursesQuery q, CancellationToken ct) => q.GetAsync(yearId, ct))
            .RequirePermission(Permissions.LessonPlans.View);
    }
}
