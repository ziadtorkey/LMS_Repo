using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Roles;

public static class RoleEndpoints
{
    private const int MaxNameLength = 200;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.Permissions, () => Results.Ok(PermissionGroups.Assignable))
            .RequirePermission(Permissions.Roles.View);
        app.MapGet(Routes.Roles, ListAsync).RequirePermission(Permissions.Roles.View);
        app.MapPost(Routes.Roles, CreateAsync).RequirePermission(Permissions.Roles.Create);
        app.MapPut(Routes.RolePattern, UpdateAsync).RequirePermission(Permissions.Roles.Update);
        app.MapDelete(Routes.RolePattern, DeleteAsync).RequirePermission(Permissions.Roles.Delete);
    }

    private static async Task<IResult> ListAsync(LmsDbContext db, CancellationToken ct)
    {
        var roles = await db.Roles.OrderByDescending(r => r.IsBuiltIn).ThenBy(r => r.CreatedAt).ThenBy(r => r.NameEn)
            .ToListAsync(ct);
        return Results.Ok(await ToDtosAsync(db, roles, ct));
    }

    private static async Task<IResult> CreateAsync(
        CreateRoleRequest request, HttpContext http, LmsDbContext db, ICurrentContext context,
        CallerPermissions caller, IRolePermissionCache cache, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.Roles.Create, ct) is { } denied)
        {
            return denied;
        }

        var invalid = Validate(request.NameAr, request.NameEn, request.BaseKind);
        if (invalid is not null)
        {
            return invalid;
        }

        var permissions = request.Permissions?.Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (await CheckPermissionsAsync(permissions, caller, null, ct) is { } problem)
        {
            return problem;
        }

        var nameAr = request.NameAr.Trim();
        var nameEn = request.NameEn.Trim();
        if (await NameTakenAsync(db, nameAr, nameEn, null, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.name_taken);
        }

        var organizationId = context.OrganizationId!.Value;
        var role = new Role(organizationId, null, nameAr, nameEn, Enum.Parse<RoleBaseKind>(request.BaseKind));
        db.Roles.Add(role);
        db.RolePermissions.AddRange(permissions.Select(p => new RolePermission(organizationId, role.Id, p)));
        await db.SaveChangesAsync(ct);
        cache.Invalidate(role.Id);

        return Results.Created(Routes.Role(role.Id), (await ToDtosAsync(db, [role], ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateRoleRequest request, HttpContext http, LmsDbContext db,
        CallerPermissions caller, IRolePermissionCache cache, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.Roles.Update, ct) is { } denied)
        {
            return denied;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return ApiProblems.NotFound();
        }

        var invalid = Validate(request.NameAr, request.NameEn, role.BaseKind.ToString());
        if (invalid is not null)
        {
            return invalid;
        }

        var requested = request.Permissions?.Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (await CheckPermissionsAsync(requested, caller, role.BuiltInKey, ct) is { } problem)
        {
            return problem;
        }

        var nameAr = request.NameAr.Trim();
        var nameEn = request.NameEn.Trim();
        if (await NameTakenAsync(db, nameAr, nameEn, id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.name_taken);
        }

        role.Rename(nameAr, nameEn);
        var current = await db.RolePermissions.Where(p => p.RoleId == id).ToListAsync(ct);
        db.RolePermissions.RemoveRange(current.Where(p => !requested.Contains(p.Permission)));
        db.RolePermissions.AddRange(requested
            .Where(p => current.All(c => c.Permission != p))
            .Select(p => new RolePermission(role.OrganizationId, id, p)));
        await db.SaveChangesAsync(ct);
        cache.Invalidate(id);

        return Results.Ok((await ToDtosAsync(db, [role], ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, IRolePermissionCache cache, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.Roles.Delete, ct) is { } denied)
        {
            return denied;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return ApiProblems.NotFound();
        }

        if (role.IsBuiltIn)
        {
            return ApiProblems.Conflict(ProblemCodes.built_in_role);
        }

        // The caller is at the client root (checked above), so the scope filter sees every assignment of the client.
        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.role_in_use);
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        cache.Invalidate(id);
        return Results.NoContent();
    }

    private static IResult? Validate(string? nameAr, string? nameEn, string? baseKind)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(nameAr) || nameAr.Trim().Length > MaxNameLength)
        {
            errors[nameof(CreateRoleRequest.NameAr)] = ["role_name_invalid"];
        }

        if (string.IsNullOrWhiteSpace(nameEn) || nameEn.Trim().Length > MaxNameLength)
        {
            errors[nameof(CreateRoleRequest.NameEn)] = ["role_name_invalid"];
        }

        if (!Enum.TryParse<RoleBaseKind>(baseKind, out var kind) || !Enum.IsDefined(kind))
        {
            errors[nameof(CreateRoleRequest.BaseKind)] = ["role_base_kind_invalid"];
        }

        return errors.Count == 0 ? null : ValidationFailed(errors);
    }

    private static IResult ValidationFailed(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    private static async Task<IResult?> CheckPermissionsAsync(
        IReadOnlyCollection<string> requested, CallerPermissions caller, string? builtInKey, CancellationToken ct)
    {
        var code = RolePermissionRules.Validate(requested, await caller.GetAsync(ct), caller.IsPlatformAdmin, builtInKey);
        return code switch
        {
            null => null,
            ProblemCodes.permission_escalation => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                extensions: new Dictionary<string, object?> { ["code"] = code }),
            ProblemCodes.last_admin_permission => ApiProblems.Conflict(code),
            ProblemCodes.validation_failed => ValidationFailed(new Dictionary<string, string[]>
            {
                [nameof(CreateRoleRequest.Permissions)] = ["permission_unknown"],
            }),
            _ => ApiProblems.BadRequest(code),
        };
    }

    private static Task<bool> NameTakenAsync(LmsDbContext db, string nameAr, string nameEn, Guid? exceptId, CancellationToken ct) =>
        db.Roles.AnyAsync(r => r.Id != exceptId && (r.NameAr == nameAr || r.NameEn == nameEn), ct);

    private static async Task<IReadOnlyList<RoleDto>> ToDtosAsync(
        LmsDbContext db, IReadOnlyList<Role> roles, CancellationToken ct)
    {
        var ids = roles.Select(r => r.Id).ToList();
        var permissions = (await db.RolePermissions.Where(p => ids.Contains(p.RoleId))
                .Select(p => new { p.RoleId, p.Permission }).ToListAsync(ct))
            .ToLookup(p => p.RoleId, p => p.Permission);
        var counts = await db.RoleAssignments.Where(a => ids.Contains(a.RoleId))
            .GroupBy(a => a.RoleId).Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);

        return roles.Select(r => new RoleDto(
            r.Id, r.BuiltInKey, r.NameAr, r.NameEn, r.BaseKind.ToString(), r.IsBuiltIn,
            permissions[r.Id].Order(StringComparer.Ordinal).ToList(), counts.GetValueOrDefault(r.Id))).ToList();
    }
}
