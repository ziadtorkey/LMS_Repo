using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Access;

/// <summary>A role assignment that is usable today, with the names needed to present it.</summary>
public sealed record ValidAssignment
{
    public Guid AssignmentId { get; init; }
    public Guid UserId { get; init; }
    public Guid OrganizationId { get; init; }
    public string OrganizationNameAr { get; init; } = string.Empty;
    public string OrganizationNameEn { get; init; } = string.Empty;
    public Guid OrgUnitId { get; init; }
    public string OrgUnitPath { get; init; } = string.Empty;
    public string OrgUnitNameAr { get; init; } = string.Empty;
    public string OrgUnitNameEn { get; init; } = string.Empty;
    public Guid RoleId { get; init; }
    public string RoleNameAr { get; init; } = string.Empty;
    public string RoleNameEn { get; init; } = string.Empty;
}
public static class ValidAssignmentQuery
{
    /// <summary>
    /// The single definition of "a valid assignment" shared by the context middleware and GET /api/me: owned by the
    /// user, valid today (inclusive window), and neither the assignment, its role, its unit nor its client deleted,
    /// with role and unit belonging to the assignment's client.
    /// </summary>
    public static IQueryable<ValidAssignment> ValidAssignmentsFor(this LmsDbContext db, Guid userId, DateOnly today) =>
        db.ValidAssignments(today).Where(a => a.UserId == userId);

    /// <summary>The same definition for several users at once (one query, e.g. a notification fan-out).</summary>
    public static IQueryable<ValidAssignment> ValidAssignmentsForUsers(
        this LmsDbContext db, IReadOnlyCollection<Guid> userIds, DateOnly today) =>
        db.ValidAssignments(today).Where(a => userIds.Contains(a.UserId));

    private static IQueryable<ValidAssignment> ValidAssignments(this LmsDbContext db, DateOnly today) =>
        // ISOLATION-BYPASS: the caller has no (or another) scope when asking "which contexts can I pick?".
        // Every skipped condition (soft delete, client match) is re-applied explicitly in the where clause.
        from a in db.RoleAssignments.IgnoreQueryFilters()
        join r in db.Roles.IgnoreQueryFilters() on a.RoleId equals r.Id
        join u in db.OrgUnits.IgnoreQueryFilters() on a.OrgUnitId equals u.Id
        join o in db.Organizations.IgnoreQueryFilters() on a.OrganizationId equals o.Id
        where !a.IsDeleted && !r.IsDeleted && !u.IsDeleted && !o.IsDeleted
              && r.OrganizationId == a.OrganizationId && u.OrganizationId == a.OrganizationId
              && o.Status == OrganizationStatus.Active // a suspended client has no valid contexts
              // D15: a unit that is Inactive, or has an Inactive ancestor, is not a valid context. ISOLATION-BYPASS: the
              // inactive ancestor lies above the assignment's scope; the client is matched and deleted units excluded by hand.
              && u.Status == OrgUnitStatus.Active
              && !db.OrgUnits.IgnoreQueryFilters().Any(x => x.OrganizationId == a.OrganizationId && !x.IsDeleted
                                                           && x.Status == OrgUnitStatus.Inactive && u.Path.StartsWith(x.Path))
              && (a.ValidFrom == null || a.ValidFrom <= today)
              && (a.ValidTo == null || a.ValidTo >= today)
              // Person status gate (M2 Task 6): the holder's profile of the role's base kind in this client, when there is
              // one, must be Active (and, for staff, not past ExpiresOn). No profile = a legacy bare user: no gate.
              // ISOLATION-BYPASS: the profile lies outside the caller's scope (or there is none yet); matched by user
              // and client explicitly, deleted profiles excluded.
              && !(r.BaseKind == RoleBaseKind.Staff
                   && db.Set<StaffProfile>().IgnoreQueryFilters().Any(p =>
                       p.UserId == a.UserId && p.OrganizationId == a.OrganizationId && !p.IsDeleted
                       && (p.Status != PersonStatus.Active || (p.ExpiresOn != null && p.ExpiresOn < today))))
              // ISOLATION-BYPASS: same as above, for the student profile of the user in this client.
              && !(r.BaseKind == RoleBaseKind.Student
                   && db.Set<StudentProfile>().IgnoreQueryFilters().Any(p =>
                       p.UserId == a.UserId && p.OrganizationId == a.OrganizationId && !p.IsDeleted
                       && p.Status != PersonStatus.Active))
        select new ValidAssignment
        {
            AssignmentId = a.Id,
            UserId = a.UserId,
            OrganizationId = a.OrganizationId,
            OrganizationNameAr = o.NameAr,
            OrganizationNameEn = o.NameEn,
            OrgUnitId = u.Id,
            OrgUnitPath = u.Path,
            OrgUnitNameAr = u.NameAr,
            OrgUnitNameEn = u.NameEn,
            RoleId = r.Id,
            RoleNameAr = r.NameAr,
            RoleNameEn = r.NameEn,
        };
}