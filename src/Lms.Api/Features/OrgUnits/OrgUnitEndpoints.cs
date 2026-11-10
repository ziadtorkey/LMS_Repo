using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.OrgUnits;

/// <summary>
/// Org tree endpoints. Each has the route-level permission (first gate) and, when it names a unit, a UnitAccess
/// check that evaluates the permission at that unit and answers 404 for units the caller cannot see.
/// </summary>
public static class OrgUnitEndpoints
{
    public static IEndpointRouteBuilder MapOrgUnitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.OrgUnits, ListAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.View);
        app.MapGet(Routes.OrgUnitPattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.View);
        app.MapPost(Routes.OrgUnits, CreateAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.Create);
        app.MapPut(Routes.OrgUnitPattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.Update);
        app.MapPut(Routes.OrgUnitStatusPattern, SetStatusAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.Update);
        app.MapPut(Routes.OrgUnitAcademicFlagsPattern, AcademicFlagsAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.Update);
        app.MapDelete(Routes.OrgUnitPattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.Delete);
        return app;
    }

    private static async Task<IResult> ListAsync(
        OrgUnitQueries queries, string? search, bool? includeInactive, CancellationToken ct) =>
        Results.Ok(await queries.ListAsync(search, includeInactive ?? false, ct));

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, OrgUnitQueries queries, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.OrgUnits.View, id, ct);
        return access.Problem ?? Results.Ok(await queries.GetNodeAsync(access.Unit!, ct));
    }

    private static async Task<IResult> CreateAsync(
        CreateOrgUnitRequest request, HttpContext http, LmsDbContext db, IOrgTree tree, OrgUnitQueries queries, CancellationToken ct)
    {
        var errors = OrgUnitValidators.Validate(request);
        if (errors.Count > 0)
        {
            return OrgUnitValidators.ToProblem(errors);
        }

        var access = await http.CheckUnitAsync(Permissions.OrgUnits.Create, request.ParentId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var parent = access.Unit!;
        var code = request.Code.Trim();
        var levelProblem = await CheckLevelAsync(db, request.LevelTypeId, parent, null, ct);
        if (levelProblem is not null)
        {
            return levelProblem;
        }

        if (await tree.CodeExistsAsync(parent.OrganizationId, code, null, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.code_taken);
        }

        var unit = OrgUnit.CreateChild(parent, request.LevelTypeId, request.NameAr.Trim(), request.NameEn.Trim(), code);
        db.OrgUnits.Add(unit);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.OrgUnit(unit.Id), await queries.GetNodeAsync(unit, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateOrgUnitRequest request, HttpContext http, LmsDbContext db, IOrgTree tree, OrgUnitQueries queries,
        CancellationToken ct)
    {
        var errors = OrgUnitValidators.Validate(request);
        if (errors.Count > 0)
        {
            return OrgUnitValidators.ToProblem(errors);
        }

        var access = await http.CheckUnitAsync(Permissions.OrgUnits.Update, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var code = request.Code.Trim();
        var parent = unit.ParentId is { } parentId ? await tree.FindVisibleAsync(parentId, ct) : null;
        var levelProblem = await CheckLevelAsync(db, request.LevelTypeId, parent, unit, ct);
        if (levelProblem is not null)
        {
            return levelProblem;
        }

        if (await tree.CodeExistsAsync(unit.OrganizationId, code, unit.Id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.code_taken);
        }

        unit.Update(request.LevelTypeId, request.NameAr.Trim(), request.NameEn.Trim(), code);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await queries.GetNodeAsync(unit, ct));
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id, SetOrgUnitStatusRequest request, HttpContext http, LmsDbContext db, OrgUnitQueries queries, CancellationToken ct)
    {
        var errors = OrgUnitValidators.Validate(request);
        if (errors.Count > 0)
        {
            return OrgUnitValidators.ToProblem(errors);
        }

        var access = await http.CheckUnitAsync(Permissions.OrgUnits.Update, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (request.Status == OrgUnitStatuses.Inactive)
        {
            if (unit.ParentId is null)
            {
                return ApiProblems.Conflict(ProblemCodes.root_unit);
            }

            unit.Deactivate();
        }
        else
        {
            unit.Activate();
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await queries.GetNodeAsync(unit, ct));
    }

    private static async Task<IResult> AcademicFlagsAsync(
        Guid id, OrgUnitAcademicFlagsRequest request, HttpContext http, AcademicFlagsHandler handler, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.OrgUnits.Update, id, ct);
        return access.Problem ?? await handler.HandleAsync(access.Unit!, request, ct);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, DeleteOrgUnitHandler handler, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.OrgUnits.Delete, id, ct);
        return access.Problem ?? await handler.HandleAsync(access.Unit!, ct);
    }

    /// <summary>The level type must belong to the client and be deeper than the parent's (and shallower than the unit's own children).</summary>
    private static async Task<IResult?> CheckLevelAsync(
        LmsDbContext db, Guid levelTypeId, OrgUnit? parent, OrgUnit? self, CancellationToken ct)
    {
        var level = await db.LevelTypes.FirstOrDefaultAsync(l => l.Id == levelTypeId, ct);
        if (level is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
        }

        if (parent is not null)
        {
            var parentLevel = await db.LevelTypes.FirstOrDefaultAsync(l => l.Id == parent.LevelTypeId, ct);
            if (parentLevel is not null && level.Order <= parentLevel.Order)
            {
                return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
            }
        }

        if (self is not null)
        {
            var childLevels = db.OrgUnits.Where(u => u.ParentId == self.Id)
                .Join(db.LevelTypes, u => u.LevelTypeId, l => l.Id, (_, l) => l.Order);
            if (await childLevels.AnyAsync(order => order <= level.Order, ct))
            {
                return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
            }
        }

        return null;
    }
}