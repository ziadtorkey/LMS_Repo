using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Announcements;

public sealed record AudienceTarget(string Kind, Guid? TargetId, bool IncludeSubtree);

/// <summary>The people an announcement reaches, each with the chip they were reached as (<c>Staff</c> before <c>Students</c> before <c>Guardians</c> when a person is in several).</summary>
public sealed record Audience(IReadOnlyDictionary<Guid, string> Users)
{
    public int Count => Users.Count;
}

/// <summary>
/// Resolves an announcement audience (proposal #613 section 3, site 4 of the isolation list). It reads through the caller's context, so the query filters
/// clip every target to the author's own scope (a target outside it contributes nobody) and the client filter keeps other clients out; no filter is bypassed.
/// Students: active people with a login, placed per target kind. Staff: holders of a valid staff-kind role assignment in the target, or the teachers of its
/// class courses. Guardians: guardians with a portal account (D30) linked to a reached student. Inactive users and expired assignments are excluded; each person
/// appears once (a guardian of two reached students gets one copy). The actor is never in the audience.
/// </summary>
public sealed class AudienceResolver(LmsDbContext db)
{
    public async Task<Audience> ResolveAsync(
        IReadOnlyList<AudienceTarget> targets, IReadOnlyCollection<string> chips, Guid? excludeUserId, DateOnly today, CancellationToken ct)
    {
        var studentIds = new HashSet<Guid>();
        var staffUserIds = new HashSet<Guid>();
        foreach (var target in targets)
        {
            if (chips.Contains(AnnouncementRoleFilters.Students) || chips.Contains(AnnouncementRoleFilters.Guardians))
            {
                studentIds.UnionWith(await StudentsAsync(target, today, ct));
            }

            if (chips.Contains(AnnouncementRoleFilters.Staff))
            {
                staffUserIds.UnionWith(await StaffAsync(target, today, ct));
            }
        }

        var users = new Dictionary<Guid, string>();
        foreach (var userId in staffUserIds)
        {
            users[userId] = AnnouncementRoleFilters.Staff;
        }

        if (chips.Contains(AnnouncementRoleFilters.Students))
        {
            foreach (var userId in await StudentUsersAsync(studentIds, ct))
            {
                users.TryAdd(userId, AnnouncementRoleFilters.Students);
            }
        }

        if (chips.Contains(AnnouncementRoleFilters.Guardians))
        {
            foreach (var userId in await GuardianUsersAsync(studentIds, ct))
            {
                users.TryAdd(userId, AnnouncementRoleFilters.Guardians);
            }
        }

        if (excludeUserId is { } actor)
        {
            users.Remove(actor);
        }

        var active = new HashSet<Guid>();
        foreach (var chunk in users.Keys.Chunk(500))
        {
            active.UnionWith(await db.Set<User>().AsNoTracking().Where(u => chunk.Contains(u.Id) && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct));
        }

        return new Audience(users.Where(u => active.Contains(u.Key)).ToDictionary(u => u.Key, u => u.Value));
    }

    private async Task<HashSet<Guid>> StudentsAsync(AudienceTarget target, DateOnly today, CancellationToken ct)
    {
        var active = db.Set<StudentProfile>().Where(s => s.Status == PersonStatus.Active);
        IQueryable<Guid> ids;
        switch (target.Kind)
        {
            case AnnouncementTargetKinds.Organization:
                ids = active.Select(s => s.Id);
                break;
            case AnnouncementTargetKinds.Unit when target.TargetId is { } unitId:
                var path = await db.Set<OrgUnit>().Where(u => u.Id == unitId).Select(u => u.Path).FirstOrDefaultAsync(ct);
                if (path is null)
                {
                    return [];
                }

                ids = active.Where(s => target.IncludeSubtree ? s.OrgUnitPath.StartsWith(path) : s.OrgUnitPath == path).Select(s => s.Id);
                break;
            case AnnouncementTargetKinds.Level when target.TargetId is { } levelId:
                var classIds = db.Set<Class>().Where(c => db.Set<OrgUnit>().Any(u => u.Id == c.OrgUnitId && u.LevelTypeId == levelId)).Select(c => c.Id);
                ids = db.Set<ClassStudent>().Where(p => p.Status == ClassStudent.ActiveStatus && classIds.Contains(p.ClassId)).Select(p => p.StudentId);
                break;
            case AnnouncementTargetKinds.Class when target.TargetId is { } classId:
                ids = db.Set<ClassStudent>().Where(p => p.Status == ClassStudent.ActiveStatus && p.ClassId == classId).Select(p => p.StudentId);
                break;
            case AnnouncementTargetKinds.ClassCourse when target.TargetId is { } classCourseId:
                ids = db.Set<CourseEnrollment>().Where(e => e.Status == CourseEnrollment.ActiveStatus && e.ClassCourseId == classCourseId).Select(e => e.StudentId);
                break;
            case AnnouncementTargetKinds.Group when target.TargetId is { } groupId:
                ids = db.Set<GroupMember>().Where(m => m.GroupId == groupId).Select(m => m.StudentId);
                break;
            default:
                return [];
        }

        var activeIds = active.Select(s => s.Id);
        return [.. await ids.Where(id => activeIds.Contains(id)).Distinct().ToListAsync(ct)];
    }

    private async Task<HashSet<Guid>> StaffAsync(AudienceTarget target, DateOnly today, CancellationToken ct)
    {
        var assignments = db.Set<RoleAssignment>().Where(a => (a.ValidFrom == null || a.ValidFrom <= today) && (a.ValidTo == null || a.ValidTo >= today))
            .Join(db.Set<Role>().Where(r => r.BaseKind == RoleBaseKind.Staff), a => a.RoleId, r => r.Id, (a, _) => a);
        var teachers = db.Set<ClassCourseTeacher>().Where(t => t.To == null || t.To >= today);
        IQueryable<Guid> users;
        switch (target.Kind)
        {
            case AnnouncementTargetKinds.Organization:
                users = assignments.Select(a => a.UserId);
                break;
            case AnnouncementTargetKinds.Unit when target.TargetId is { } unitId:
                var path = await db.Set<OrgUnit>().Where(u => u.Id == unitId).Select(u => u.Path).FirstOrDefaultAsync(ct);
                if (path is null)
                {
                    return [];
                }

                users = assignments.Where(a => target.IncludeSubtree ? a.OrgUnitPath.StartsWith(path) : a.OrgUnitPath == path).Select(a => a.UserId);
                break;
            case AnnouncementTargetKinds.Level when target.TargetId is { } levelId:
                var inLevel = db.Set<Class>().Where(c => db.Set<OrgUnit>().Any(u => u.Id == c.OrgUnitId && u.LevelTypeId == levelId)).Select(c => c.Id);
                users = teachers.Where(t => db.Set<ClassCourse>().Any(cc => cc.Id == t.ClassCourseId && inLevel.Contains(cc.ClassId))).Select(t => t.UserId);
                break;
            case AnnouncementTargetKinds.Class when target.TargetId is { } classId:
                users = teachers.Where(t => db.Set<ClassCourse>().Any(cc => cc.Id == t.ClassCourseId && cc.ClassId == classId)).Select(t => t.UserId);
                break;
            case AnnouncementTargetKinds.ClassCourse when target.TargetId is { } classCourseId:
                users = teachers.Where(t => t.ClassCourseId == classCourseId).Select(t => t.UserId);
                break;
            case AnnouncementTargetKinds.Group when target.TargetId is { } groupId:
                users = teachers.Where(t => db.Set<StudentGroup>().Any(g => g.Id == groupId && g.ClassCourseId == t.ClassCourseId)).Select(t => t.UserId);
                break;
            default:
                return [];
        }

        return [.. await users.Distinct().ToListAsync(ct)];
    }

    private async Task<List<Guid>> StudentUsersAsync(HashSet<Guid> studentIds, CancellationToken ct) =>
        await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id) && s.UserId != null).Select(s => s.UserId!.Value).Distinct().ToListAsync(ct);

    private async Task<List<Guid>> GuardianUsersAsync(HashSet<Guid> studentIds, CancellationToken ct)
    {
        var guardianIds = db.Set<StudentGuardian>().Where(l => studentIds.Contains(l.StudentId)).Select(l => l.GuardianId);
        return await db.Set<GuardianProfile>().Where(g => guardianIds.Contains(g.Id) && g.UserId != null).Select(g => g.UserId!.Value).Distinct().ToListAsync(ct);
    }
}
