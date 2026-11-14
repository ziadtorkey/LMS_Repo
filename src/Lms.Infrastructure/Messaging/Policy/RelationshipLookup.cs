using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging.Policy;

/// <summary>
/// The relationships and roles behind the messaging rules (M6 Task 11, plan site 3). A message partner is in another unit of the same client than the caller, so every
/// read here is outside the caller's unit scope: each is limited to one client, live rows and the two users in question, and only a yes or no (and the built-in role keys
/// of a user) leave. Nothing returned names a user, a class or a unit.
/// </summary>
public sealed class RelationshipLookup(LmsDbContext db)
{
    private static readonly HashSet<string> FamilyRoles = [BuiltInRoles.Student, BuiltInRoles.Guardian];
    private static readonly HashSet<string> StaffWithStudents = [BuiltInRoles.Teacher, BuiltInRoles.Counselor, BuiltInRoles.Supervisor];

    /// <summary>The built-in role keys the user holds in the client on the day (a valid assignment of a built-in role), with the unit paths of those assignments.</summary>
    public async Task<IReadOnlyDictionary<string, List<string>>> RolesAsync(Guid organizationId, Guid userId, DateOnly today, IReadOnlyCollection<string> roleKeys, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the other person of a conversation holds their role assignments outside the caller scope; limited to the client, live rows, one user, built-in
        // roles of the matrix and valid on the day; only role keys and the paths of those assignments are used, and only for the policy decision.
        var rows = await (from a in db.Set<RoleAssignment>().IgnoreQueryFilters()
                          join r in db.Set<Role>().IgnoreQueryFilters() on a.RoleId equals r.Id
                          where !a.IsDeleted && !r.IsDeleted && a.OrganizationId == organizationId && r.OrganizationId == organizationId && a.UserId == userId
                                && r.BuiltInKey != null && roleKeys.Contains(r.BuiltInKey) && (a.ValidFrom == null || a.ValidFrom <= today) && (a.ValidTo == null || a.ValidTo >= today)
                          select new { Key = r.BuiltInKey!, a.OrgUnitPath }).ToListAsync(ct);
        return rows.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.Select(r => r.OrgUnitPath).Distinct().ToList());
    }

    /// <summary>
    /// True when the two users, holding the given roles, are related as the rules mean it: a student or guardian with a teacher of one of the student's class courses, a
    /// counselor whose assignment covers the student's unit, or a supervisor of a course the student takes. Staff with staff have no relationship rule (they use
    /// <c>Allow</c>), so that answer is false.
    /// </summary>
    public async Task<bool> RelatedAsync(
        Guid organizationId, string roleA, Guid userA, string roleB, Guid userB, DateOnly today,
        IReadOnlyDictionary<string, List<string>> assignmentsA, IReadOnlyDictionary<string, List<string>> assignmentsB, CancellationToken ct)
    {
        if (FamilyRoles.Contains(roleB) && StaffWithStudents.Contains(roleA))
        {
            return await RelatedAsync(organizationId, roleB, userB, roleA, userA, today, assignmentsB, assignmentsA, ct);
        }

        if (!FamilyRoles.Contains(roleA) || !StaffWithStudents.Contains(roleB))
        {
            return false;
        }

        var studentIds = await StudentsOfAsync(organizationId, roleA, userA, ct);
        if (studentIds.Count == 0)
        {
            return false;
        }

        switch (roleB)
        {
            case BuiltInRoles.Teacher:
                var taught = await TaughtClassCoursesAsync(organizationId, userB, today, ct);
                return taught.Count > 0 && await EnrolledAsync(organizationId, studentIds, taught, today, ct);
            case BuiltInRoles.Supervisor:
                var supervised = await SupervisedClassCoursesAsync(organizationId, userB, ct);
                return supervised.Count > 0 && await EnrolledAsync(organizationId, studentIds, supervised, today, ct);
            default: // Counselor: the assignment covers the student's home unit (the unit itself or an ancestor)
                var paths = assignmentsB.GetValueOrDefault(BuiltInRoles.Counselor) ?? [];
                if (paths.Count == 0)
                {
                    return false;
                }

                // ISOLATION-BYPASS: the student's home unit lies outside the caller scope; client matched, live rows, the given students only; only the path is compared.
                var homes = await db.Set<StudentProfile>().IgnoreQueryFilters()
                    .Where(s => !s.IsDeleted && s.OrganizationId == organizationId && studentIds.Contains(s.Id)).Select(s => s.OrgUnitPath).ToListAsync(ct);
                return homes.Any(home => paths.Any(path => IsSelfOrAncestor(path, home)));
        }
    }

    /// <summary>True when the two users work in the same unit subtree: one of their units (assignments, a student's home unit, a guardian's children) is the other's or an ancestor of it.</summary>
    public async Task<bool> SameUnitAsync(
        Guid organizationId, Guid userA, Guid userB, IReadOnlyDictionary<string, List<string>> assignmentsA, IReadOnlyDictionary<string, List<string>> assignmentsB, CancellationToken ct)
    {
        var unitsA = await UnitPathsAsync(organizationId, userA, assignmentsA, ct);
        var unitsB = await UnitPathsAsync(organizationId, userB, assignmentsB, ct);
        return unitsA.Any(a => unitsB.Any(b => IsSelfOrAncestor(a, b) || IsSelfOrAncestor(b, a)));
    }

    private async Task<List<string>> UnitPathsAsync(Guid organizationId, Guid userId, IReadOnlyDictionary<string, List<string>> assignments, CancellationToken ct)
    {
        var paths = assignments.Values.SelectMany(p => p).ToList();
        var students = await StudentsOfAsync(organizationId, BuiltInRoles.Student, userId, ct);
        var children = await StudentsOfAsync(organizationId, BuiltInRoles.Guardian, userId, ct);
        var ids = students.Concat(children).Distinct().ToList();
        if (ids.Count > 0)
        {
            // ISOLATION-BYPASS: as above, the home units of the user own student profiles or children; client matched, live rows, only paths leave.
            paths.AddRange(await db.Set<StudentProfile>().IgnoreQueryFilters()
                .Where(s => !s.IsDeleted && s.OrganizationId == organizationId && ids.Contains(s.Id)).Select(s => s.OrgUnitPath).ToListAsync(ct));
        }

        return paths;
    }

    private async Task<List<Guid>> StudentsOfAsync(Guid organizationId, string role, Guid userId, CancellationToken ct)
    {
        if (role == BuiltInRoles.Student)
        {
            // ISOLATION-BYPASS: the student profile of the other person of a conversation; client matched, live rows, the user own profile only; only its id is used.
            return await db.Set<StudentProfile>().IgnoreQueryFilters()
                .Where(s => !s.IsDeleted && s.OrganizationId == organizationId && s.UserId == userId).Select(s => s.Id).ToListAsync(ct);
        }

        // ISOLATION-BYPASS: the children of a guardian live in other units than the caller; client matched, live links and profiles, the guardian user own links only; only student ids are used.
        return await (from link in db.Set<StudentGuardian>().IgnoreQueryFilters()
                      join g in db.Set<GuardianProfile>().IgnoreQueryFilters() on link.GuardianId equals g.Id
                      where !link.IsDeleted && !g.IsDeleted && g.OrganizationId == organizationId && g.UserId == userId
                      select link.StudentId).Distinct().ToListAsync(ct);
    }

    private async Task<List<Guid>> TaughtClassCoursesAsync(Guid organizationId, Guid teacherUserId, DateOnly today, CancellationToken ct) =>
        // ISOLATION-BYPASS: the class courses of a teacher in another unit than the caller; client matched, live rows, one teacher, assignment dates valid on the day; only ids are used.
        await db.Set<ClassCourseTeacher>().IgnoreQueryFilters()
            .Where(t => !t.IsDeleted && t.OrganizationId == organizationId && t.UserId == teacherUserId && t.From <= today && (t.To == null || t.To >= today))
            .Select(t => t.ClassCourseId).Distinct().ToListAsync(ct);

    private async Task<List<Guid>> SupervisedClassCoursesAsync(Guid organizationId, Guid supervisorUserId, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the courses a supervisor oversees and their class courses sit in other units than the caller; client matched, live rows, one supervisor; only ids are used.
        var courses = db.Set<CourseSupervisor>().IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.OrganizationId == organizationId && s.UserId == supervisorUserId).Select(s => s.CourseId);
        // ISOLATION-BYPASS: the class courses of the supervised courses, same limits (client, live rows, only ids are used).
        return await db.Set<ClassCourse>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && courses.Contains(c.CourseId)).Select(c => c.Id).ToListAsync(ct);
    }

    private async Task<bool> EnrolledAsync(Guid organizationId, List<Guid> studentIds, List<Guid> classCourseIds, DateOnly today, CancellationToken ct) =>
        // ISOLATION-BYPASS: enrollments of the students of the two users; client matched, live rows, active on the day; only a yes or no leaves.
        await db.Set<CourseEnrollment>().IgnoreQueryFilters()
            .AnyAsync(e => !e.IsDeleted && e.OrganizationId == organizationId && studentIds.Contains(e.StudentId) && classCourseIds.Contains(e.ClassCourseId)
                           && e.From <= today && (e.To == null || e.To >= today), ct);

    private static bool IsSelfOrAncestor(string ancestorPath, string path) =>
        OrgUnitPath.AncestorsAndSelf(path).Contains(ancestorPath);
}
