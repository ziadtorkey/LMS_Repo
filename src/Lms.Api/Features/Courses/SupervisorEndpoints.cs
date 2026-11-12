using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Courses;

/// <summary>
/// PUT /api/courses/{id}/supervisors (FR-CRS-04, D42): replaces the supervisors of a course. Needs <c>Courses.ManageSupervisors</c>
/// at the owner unit (Org Admin and Manager; a Supervisor cannot appoint supervisors) and the relationship rule of that permission.
/// Every user must hold a valid Supervisor-role assignment at the owner unit, above it or below it. "My courses" is
/// <c>GET /api/courses?supervisedByMe=true</c> (the contract's form of the plan's /api/me/supervised-courses).
/// </summary>
internal static class SupervisorEndpoints
{
    private const int MaxSupervisors = 50;

    public static IEndpointRouteBuilder MapSupervisorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut(Routes.CourseSupervisorsPattern, SetAsync).RequireAuthorization().RequirePermission(Permissions.Courses.ManageSupervisors);
        return app;
    }

    private static async Task<IResult> SetAsync(
        Guid id, SetCourseSupervisorsRequest? request, HttpContext http, LmsDbContext db, IOrgTree tree, ICurrentContext context,
        IClock clock, CancellationToken ct)
    {
        if (request?.UserIds is null || request.UserIds.Count > MaxSupervisors)
        {
            return Invalid("UserIds");
        }

        var (course, problem) = await http.CheckCourseAsync(Permissions.Courses.ManageSupervisors, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var wanted = request.UserIds.Distinct().ToList();
        if (wanted.Count > 0 && await NonSupervisorsAsync(db, course!, wanted, DateOnly.FromDateTime(clock.UtcNow), ct) is { Count: > 0 } bad)
        {
            return Invalid("UserIds", bad);
        }

        var current = await db.Set<CourseSupervisor>().Where(s => s.CourseId == id).ToListAsync(ct);
        foreach (var removed in current.Where(s => !wanted.Contains(s.UserId)))
        {
            db.Set<CourseSupervisor>().Remove(removed);
        }

        foreach (var added in wanted.Where(u => current.All(s => s.UserId != u)))
        {
            db.Set<CourseSupervisor>().Add(new CourseSupervisor(course!, added));
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await new CourseMapper(db, tree, context).SupervisorsAsync(id, ct));
    }

    /// <summary>Users without a valid Supervisor assignment at, above or below the course's owner unit.</summary>
    private static async Task<List<Guid>> NonSupervisorsAsync(
        LmsDbContext db, Course course, IReadOnlyCollection<Guid> userIds, DateOnly today, CancellationToken ct)
    {
        var path = course.OrgUnitPath;
        var supervisorRoles = db.Roles.Where(r => r.BuiltInKey == BuiltInRoles.Supervisor).Select(r => r.Id);
        var qualified = await db.ValidAssignmentsForUsers(userIds, today)
            .Where(a => a.OrganizationId == course.OrganizationId && supervisorRoles.Contains(a.RoleId)
                        && (path.StartsWith(a.OrgUnitPath) || a.OrgUnitPath.StartsWith(path)))
            .Select(a => a.UserId).Distinct().ToListAsync(ct);
        return userIds.Where(u => !qualified.Contains(u)).ToList();
    }

    private static IResult Invalid(string field, IReadOnlyList<Guid>? users = null) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Bad request",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ProblemCodes.validation_failed, ["field"] = field, ["users"] = users?.ToArray(),
            });
}
