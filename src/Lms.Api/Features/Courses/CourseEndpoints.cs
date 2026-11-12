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
        app.MapDelete(Routes.CoursePattern, CourseHandlers.DeleteAsync).RequireAuthorization()
            .RequirePermission(Permissions.Courses.Delete);
        return app;
    }
}
