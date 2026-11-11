using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Access;

/// <summary>Scoped: one filter per relationship for the whole request.</summary>
public sealed class RelationshipScope(LmsDbContext db, ICurrentContext context, IClock clock) : IRelationshipScope
{
    private readonly Dictionary<string, RelationshipFilter> _cache = [];
    private string? _roleKey;
    private bool _roleLoaded;

    public async Task<RelationshipFilter> ForAsync(string permission, CancellationToken cancellationToken)
    {
        if (context.IsPlatformAdmin)
        {
            return RelationshipFilter.Unrestricted;
        }

        var roleKey = await RoleKeyAsync(cancellationToken);
        var relationship = roleKey is null ? null : RelationshipRules.For(roleKey, permission);
        if (relationship is null)
        {
            // Custom roles and built-in roles without a rule are unrestricted inside their unit scope. A caller without
            // an active assignment reaches nothing.
            return context.AssignmentId is null ? RelationshipFilter.None : RelationshipFilter.Unrestricted;
        }

        if (_cache.TryGetValue(relationship, out var cached))
        {
            return cached;
        }

        var filter = await BuildAsync(relationship, cancellationToken);
        _cache[relationship] = filter;
        return filter;
    }

    private async Task<string?> RoleKeyAsync(CancellationToken ct)
    {
        if (!_roleLoaded)
        {
            _roleLoaded = true;
            if (context.AssignmentId is { } assignmentId)
            {
                _roleKey = await (from a in db.RoleAssignments
                                  where a.Id == assignmentId
                                  join r in db.Roles on a.RoleId equals r.Id
                                  select r.BuiltInKey).FirstOrDefaultAsync(ct);
            }
        }

        return _roleKey;
    }

    private async Task<RelationshipFilter> BuildAsync(string relationship, CancellationToken ct)
    {
        if (context.UserId is not { } userId)
        {
            return RelationshipFilter.None;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var classCourses = new HashSet<Guid>();
        var homeroom = new HashSet<Guid>();
        var supervised = new HashSet<Guid>();
        switch (relationship)
        {
            case Relationships.OwnClassCourses:
                classCourses.UnionWith(await db.Set<ClassCourseTeacher>()
                    .Where(t => t.UserId == userId && t.From <= today && (t.To == null || t.To >= today))
                    .Select(t => t.ClassCourseId).ToListAsync(ct));
                homeroom.UnionWith(await db.Set<Class>()
                    .Where(c => c.HomeroomTeacherUserId == userId && c.Status == Class.ActiveStatus)
                    .Select(c => c.Id).ToListAsync(ct));
                break;
            case Relationships.SupervisedCourses:
                supervised.UnionWith(await db.Set<CourseSupervisor>()
                    .Where(s => s.UserId == userId).Select(s => s.CourseId).ToListAsync(ct));
                classCourses.UnionWith(await db.Set<ClassCourse>()
                    .Where(cc => supervised.Contains(cc.CourseId)).Select(cc => cc.Id).ToListAsync(ct));
                break;
            case Relationships.OwnEnrollments:
                classCourses.UnionWith(await db.Set<CourseEnrollment>()
                    .Where(e => e.Status == CourseEnrollment.ActiveStatus
                        && db.Set<StudentProfile>().Any(s => s.Id == e.StudentId && s.UserId == userId))
                    .Select(e => e.ClassCourseId).ToListAsync(ct));
                break;
            default:
                return RelationshipFilter.None;
        }

        var rows = await db.Set<ClassCourse>().Where(cc => classCourses.Contains(cc.Id))
            .Select(cc => new { cc.ClassId, cc.CourseId }).ToListAsync(ct);
        var classes = rows.Select(r => r.ClassId).Concat(homeroom).ToHashSet();
        var courses = rows.Select(r => r.CourseId).Concat(supervised).ToHashSet();
        return new RelationshipFilter(false, classCourses, classes, courses, homeroom);
    }
}

public static class RelationshipFilterExtensions
{
    /// <summary>
    /// Students reached through the relationship: an active enrollment in an allowed class course or an active
    /// placement in an allowed class. Unrestricted filters return the query unchanged.
    /// </summary>
    public static IQueryable<StudentProfile> Students(
        this RelationshipFilter filter, IQueryable<StudentProfile> students, LmsDbContext db)
    {
        if (filter.IsUnrestricted)
        {
            return students;
        }

        var classCourseIds = filter.ClassCourseIds;
        var classIds = filter.ClassIds;
        return students.Where(s =>
            db.Set<CourseEnrollment>().Any(e => e.StudentId == s.Id && e.Status == CourseEnrollment.ActiveStatus
                && classCourseIds.Contains(e.ClassCourseId))
            || db.Set<ClassStudent>().Any(p => p.StudentId == s.Id && p.Status == ClassStudent.ActiveStatus
                && classIds.Contains(p.ClassId)));
    }
}
