using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;

namespace Lms.Api.Features.Courses;

public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.Courses, CourseHandlers.ListAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.View);
        app.MapGet(Routes.CoursePattern, CourseHandlers.GetAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.View);
        app.MapPost(Routes.Courses, CourseHandlers.CreateAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Create);
        app.MapPut(Routes.CoursePattern, CourseHandlers.UpdateAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Update);
        app.MapPut(Routes.CourseStatusPattern, CourseHandlers.SetStatusAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Update);
        app.MapPut(Routes.CourseRestrictionsPattern, RestrictionHandlers.SetAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Update);
        app.MapDelete(Routes.CoursePattern, CourseHandlers.DeleteAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Delete);
        app.MapPost(Routes.CourseAllocations, AllocationHandlers.AllocateAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Allocate);
        app.MapDelete(Routes.CourseAllocationPattern, AllocationHandlers.RemoveAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Allocate);
        app.MapGet(Routes.UnitCoursesPattern, AllocationHandlers.UnitCoursesAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.View);
        return app;
    }
}
