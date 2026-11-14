using Lms.Api.Infrastructure;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;

namespace Lms.Api.Features.Gradebook;

/// <summary>
/// The staff grid of a class course and term. Read with <c>Grades.View</c> (a teacher's own class courses, a supervisor's supervised ones; a miss is
/// 404); only a caller who also has <c>Grades.Manage</c> runs the backfill and creates the first gradebook (L4). Writes take the term as an optional
/// <c>?termId=</c> (the current term of the class when absent).
/// </summary>
public static class GradebookEndpoints
{
    public static IEndpointRouteBuilder MapGradebookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseGradebookPattern, GetAsync).RequirePermission(Permissions.Grades.View);
        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext http, GradebookReader reader, Guid? termId, int page = 1, int pageSize = GradingLimits.GridPageSize,
        CancellationToken ct = default)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Grades.View, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var canManage = await GradebookAccess.MayAsync(http, Permissions.Grades.Manage, cc!.OrgUnitPath, ct);
        var canPublish = await GradebookAccess.MayAsync(http, Permissions.Grades.Publish, cc.OrgUnitPath, ct);
        var term = await reader.ResolveTermAsync(cc, termId, ct);
        var gradebook = term is null ? null : await reader.FindAsync(cc, term, canManage, ct);
        return gradebook is null
            ? ApiProblems.NotFound()
            : Results.Ok(await reader.BuildAsync(cc, gradebook, term!, GridPaging.From(page, pageSize), canManage, canPublish, ct));
    }
}
