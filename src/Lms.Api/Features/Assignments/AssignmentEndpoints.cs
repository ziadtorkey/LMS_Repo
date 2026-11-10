using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Assignments;

public static class AssignmentEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.OrgUnitAssignmentsPattern, ListAsync).RequirePermission(Permissions.Assignments.View);
        app.MapPost(Routes.Assignments,
                (CreateAssignmentRequest request, HttpContext http, CreateAssignmentHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, http, ct))
            .RequirePermission(Permissions.Assignments.Create);
        app.MapPut(Routes.AssignmentPattern, UpdateAsync).RequirePermission(Permissions.Assignments.Update);
        app.MapDelete(Routes.AssignmentPattern, DeleteAsync).RequirePermission(Permissions.Assignments.Delete);
    }

    private static async Task<IResult> ListAsync(
        Guid id, HttpContext http, LmsDbContext db, IClock clock, CancellationToken ct,
        bool includeDescendants = false, int page = 1, int pageSize = DefaultPageSize)
    {
        var access = await http.CheckUnitAsync(Permissions.Assignments.View, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return AssignmentQueries.Invalid(page < 1 ? nameof(page) : nameof(pageSize), "paging_invalid");
        }

        var unit = access.Unit!;
        var (items, total) = await AssignmentQueries.PageAsync(
            db, clock, AssignmentQueries.OfUnit(db, unit.Path, unit.Id, includeDescendants), (page - 1) * pageSize, pageSize, ct);
        return Results.Ok(new PagedResult<AssignmentDto>(items, total, page, pageSize));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateAssignmentRequest request, HttpContext http, LmsDbContext db, IClock clock,
        CallerPermissions caller, CancellationToken ct)
    {
        // Outside the caller's scope the assignment is invisible (404); then the permission is checked at its unit.
        var assignment = await db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assignment is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Assignments.Update, assignment.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (request.ValidFrom is { } from && request.ValidTo is { } to && to < from)
        {
            return AssignmentQueries.Invalid(nameof(request.ValidTo), "validity_range_invalid");
        }

        if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, assignment.RoleId, ct))
        {
            return Forbidden();
        }

        if (await AssignmentQueries.OverlapsAsync(
                db, assignment.UserId, assignment.RoleId, assignment.OrgUnitId, request.ValidFrom, request.ValidTo, id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.assignment_exists);
        }

        assignment.ChangeValidity(request.ValidFrom, request.ValidTo);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await AssignmentQueries.GetAsync(db, clock, id, ct));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, CallerPermissions caller, CancellationToken ct)
    {
        var assignment = await db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assignment is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Assignments.Delete, assignment.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        // Nobody removes their own access: another admin must do it (no self lock-out, no orphaned client).
        if (assignment.UserId == context.UserId)
        {
            return ApiProblems.Conflict(ProblemCodes.own_assignment);
        }

        if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, assignment.RoleId, ct))
        {
            return Forbidden();
        }

        db.RoleAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static IResult Forbidden() =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.permission_escalation });
}
