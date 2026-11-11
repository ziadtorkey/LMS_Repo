using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Audit;

/// <summary>Turns audit entries into viewer rows, resolving user, role and unit names in three batched lookups.</summary>
internal static class AuditRows
{
    public static async Task<IReadOnlyList<AuditEntryDto>> ToDtosAsync(
        LmsDbContext db, IReadOnlyList<AuditEntry> entries, CancellationToken ct)
    {
        var userIds = entries.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).Distinct().ToList();
        var assignmentIds = entries.Where(e => e.AssignmentId is not null).Select(e => e.AssignmentId!.Value).Distinct().ToList();
        var unitIds = entries.Select(e => UnitIdOf(e.OrgUnitPath)).Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();

        var orgIds = entries.Where(e => e.OrganizationId is not null).Select(e => e.OrganizationId!.Value).Distinct().ToList();

        var users = await db.Set<User>().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.DisplayNameAr, u.DisplayNameEn })
            .ToDictionaryAsync(u => u.Id, ct);

        // ISOLATION-BYPASS: assignment, role and unit names are looked up by ids taken from entries the caller may
        // already see. The caller's own scope filter must not hide the name of e.g. the assignment's unit or a
        // since-deleted role from a row they are entitled to read. Every lookup is also restricted to the organizations of
        // the returned entries, so a name from another client can never be resolved.
        var roles = await db.RoleAssignments.IgnoreQueryFilters()
            .Where(a => assignmentIds.Contains(a.Id) && orgIds.Contains(a.OrganizationId))
            .Join(db.Roles.IgnoreQueryFilters().Where(r => orgIds.Contains(r.OrganizationId)), a => a.RoleId, r => r.Id, (a, r) => new { a.Id, r.NameAr, r.NameEn })
            .ToDictionaryAsync(x => x.Id, ct);
        // ISOLATION-BYPASS: unit names of the entries' units, restricted to the entries' organizations.
        var units = await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => unitIds.Contains(u.Id) && orgIds.Contains(u.OrganizationId))
            .Select(u => new { u.Id, u.NameAr, u.NameEn })
            .ToDictionaryAsync(u => u.Id, ct);

        return entries.Select(e =>
        {
            var user = e.UserId is { } uid && users.TryGetValue(uid, out var u) ? u : null;
            var role = e.AssignmentId is { } aid && roles.TryGetValue(aid, out var r) ? r : null;
            var unit = UnitIdOf(e.OrgUnitPath) is { } unitId && units.TryGetValue(unitId, out var o) ? o : null;
            return new AuditEntryDto(
                e.Id, DateTime.SpecifyKind(e.TimestampUtc, DateTimeKind.Utc), e.UserId, user?.Email, user?.DisplayNameAr,
                user?.DisplayNameEn, role?.NameAr, role?.NameEn, unit?.NameAr, unit?.NameEn,
                e.Action.ToString(), e.EntityType, e.EntityId, e.IpAddress);
        }).ToList();
    }

    /// <summary>The unit of an entry is the last segment of its path.</summary>
    private static Guid? UnitIdOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var last = path.TrimEnd('/').Split('/')[^1];
        return Guid.TryParseExact(last, "N", out var id) ? id : null;
    }
}
