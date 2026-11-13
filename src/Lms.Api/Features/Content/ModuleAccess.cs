using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>
/// The one visibility resolver of the content module (M4a Task 3, D51): who the caller is (a student or staff), which
/// modules a student may see now, and the plan order. Every student-facing read goes through it; a miss is 404.
/// </summary>
public sealed class ModuleAccess(LmsDbContext db, ICurrentContext context)
{
    private bool? _isStudent;
    private Guid? _studentId;

    /// <summary>The caller acts as the built-in Student role (their active assignment's role).</summary>
    public async Task<bool> IsStudentAsync(CancellationToken ct)
    {
        if (_isStudent is { } known)
        {
            return known;
        }

        if (context.AssignmentId is not { } assignmentId)
        {
            return (_isStudent = false).Value;
        }

        var roleKey = await (
                from a in db.RoleAssignments
                join r in db.Roles on a.RoleId equals r.Id
                where a.Id == assignmentId
                select r.BuiltInKey)
            .FirstOrDefaultAsync(ct);
        return (_isStudent = roleKey == BuiltInRoles.Student).Value;
    }

    /// <summary>The caller's student profile (null when they have none: they then see nothing).</summary>
    public async Task<Guid?> StudentIdAsync(CancellationToken ct)
    {
        if (_studentId is null && context.UserId is { } userId)
        {
            _studentId = await db.Set<StudentProfile>().Where(s => s.UserId == userId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        }

        return _studentId;
    }

    public Task<Guid?> GroupOfAsync(Guid classCourseId, Guid studentId, CancellationToken ct) =>
        db.Set<GroupMember>().Where(m => m.ClassCourseId == classCourseId && m.StudentId == studentId)
            .Select(m => (Guid?)m.GroupId).FirstOrDefaultAsync(ct);

    /// <summary>A module row of one class course with the plan order keys of its lesson and section.</summary>
    public sealed class Placed
    {
        public required LessonModule Module { get; init; }
        public int SectionOrder { get; init; }
        public int LessonOrder { get; init; }
    }

    /// <summary>
    /// The modules a student may see now: published or inside the schedule window, in a lesson and section that are not
    /// hidden, for the whole class course or the student's group. Ordered as the plan.
    /// </summary>
    public IQueryable<Placed> VisibleTo(Guid classCourseId, Guid? groupId, DateTime nowUtc) =>
        from m in db.Set<LessonModule>()
        join l in db.Set<ClassLesson>() on m.ClassLessonId equals l.Id
        join s in db.Set<ClassSection>() on l.SectionId equals s.Id
        where m.ClassCourseId == classCourseId && !l.IsHidden && !s.IsHidden
              && (m.Visibility == LessonModule.PublishedVisibility
                  || (m.Visibility == LessonModule.ScheduledVisibility
                      && (m.VisibleFromUtc == null || m.VisibleFromUtc <= nowUtc)
                      && (m.VisibleToUtc == null || nowUtc < m.VisibleToUtc)))
              && (m.Target == LessonModule.ClassCourseTarget || (groupId != null && m.GroupId == groupId))
        orderby s.Order, l.Order, m.Order
        select new Placed { Module = m, SectionOrder = s.Order, LessonOrder = l.Order };

    /// <summary>Every module of the class course, for staff, ordered as the plan.</summary>
    public IQueryable<Placed> AllOf(Guid classCourseId) =>
        from m in db.Set<LessonModule>()
        join l in db.Set<ClassLesson>() on m.ClassLessonId equals l.Id
        join s in db.Set<ClassSection>() on l.SectionId equals s.Id
        where m.ClassCourseId == classCourseId
        orderby s.Order, l.Order, m.Order
        select new Placed { Module = m, SectionOrder = s.Order, LessonOrder = l.Order };
}
