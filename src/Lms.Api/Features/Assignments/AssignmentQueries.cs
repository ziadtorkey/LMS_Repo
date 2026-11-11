using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Access;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Assignments;

/// <summary>Shared reads and checks of the assignment endpoints.</summary>
internal static class AssignmentQueries
{
    // The assignment entity is selected whole: DateOnly columns projected straight out of a join do not materialize
    // with the MySQL provider, entity materialization does.
    private sealed record Row(
        RoleAssignment A, string? Email, string UserNameAr, string UserNameEn, string RoleNameAr, string RoleNameEn,
        string UnitNameAr, string UnitNameEn);

    public static IQueryable<RoleAssignment> OfUnit(LmsDbContext db, string unitPath, Guid unitId, bool includeDescendants) =>
        includeDescendants
            ? db.RoleAssignments.Where(a => a.OrgUnitPath.StartsWith(unitPath))
            : db.RoleAssignments.Where(a => a.OrgUnitId == unitId);

    public static async Task<(IReadOnlyList<AssignmentDto> Items, int Total)> PageAsync(
        LmsDbContext db, IClock clock, IQueryable<RoleAssignment> assignments, int skip, int take, CancellationToken ct)
    {
        var rows =
            from a in assignments
            join u in db.Users on a.UserId equals u.Id
            join r in db.Roles on a.RoleId equals r.Id
            join n in db.OrgUnits on a.OrgUnitId equals n.Id
            orderby a.OrgUnitPath, u.DisplayNameEn, a.ValidFrom, a.Id
            select new Row(a, u.Email, u.DisplayNameAr, u.DisplayNameEn, r.NameAr, r.NameEn, n.NameAr, n.NameEn);

        var total = await rows.CountAsync(ct);
        var page = await rows.Skip(skip).Take(take).ToListAsync(ct);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return (page.Select(r => new AssignmentDto(
            r.A.Id, r.A.UserId, r.Email ?? string.Empty, r.UserNameAr, r.UserNameEn, r.A.RoleId, r.RoleNameAr, r.RoleNameEn,
            r.A.OrgUnitId, r.UnitNameAr, r.UnitNameEn, r.A.ValidFrom, r.A.ValidTo, r.A.IsValidOn(today))).ToList(), total);
    }

    /// <summary>The assignment with its user, role and unit names; null when one of them is gone (the join finds nothing).</summary>
    public static async Task<AssignmentDto?> GetAsync(LmsDbContext db, IClock clock, Guid id, CancellationToken ct) =>
        (await PageAsync(db, clock, db.RoleAssignments.Where(a => a.Id == id), 0, 1, ct)).Items.FirstOrDefault();

    /// <summary>The permissions of a role (empty when the role is gone).</summary>
    public static async Task<IReadOnlySet<string>> RolePermissionsAsync(LmsDbContext db, Guid roleId, CancellationToken ct) =>
        (await db.RolePermissions.Where(p => p.RoleId == roleId).Select(p => p.Permission).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>True when the caller may hand out (or take away) the role: the escalation guard of D16.</summary>
    public static async Task<bool> CallerMayManageRoleAsync(
        LmsDbContext db, CallerPermissions caller, Guid roleId, CancellationToken ct) =>
        RoleAssignmentRules.CanAssign(
            await RolePermissionsAsync(db, roleId, ct), await caller.GetAsync(ct), caller.IsPlatformAdmin);

    /// <summary>True when another live assignment of the same user, role and unit overlaps the given validity window.</summary>
    public static async Task<bool> OverlapsAsync(
        LmsDbContext db, Guid userId, Guid roleId, Guid orgUnitId, DateOnly? from, DateOnly? to, Guid? exceptId,
        CancellationToken ct)
    {
        var others = await db.RoleAssignments
            .Where(a => a.UserId == userId && a.RoleId == roleId && a.OrgUnitId == orgUnitId && a.Id != exceptId)
            .ToListAsync(ct); // whole entities: see the note on Row
        return others.Any(o =>
            (from ?? DateOnly.MinValue) <= (o.ValidTo ?? DateOnly.MaxValue)
            && (o.ValidFrom ?? DateOnly.MinValue) <= (to ?? DateOnly.MaxValue));
    }

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
}
