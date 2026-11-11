using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Enrollment;

/// <summary>
/// Keeps enrollments in step with the courses of a class: a new (or newly mandatory) course enrolls the students already
/// placed, removing a course is vetoed while manual or exceptional enrollments or group members depend on it. Runs inside
/// the caller's transaction and saves.
/// </summary>
public sealed class EnrollmentClassCourseHandler(
    LmsDbContext db, EnrollmentEngine engine, PlacementGuard guard, IClock clock) : IClassCourseLifecycleHandler
{
    public async Task OnAddedAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        if (!classCourse.IsOptional)
        {
            await EnrollPlacedStudentsAsync(classCourse, cancellationToken);
        }
    }

    public async Task OnOptionalChangedAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        // Becoming optional keeps the existing enrollments; becoming mandatory enrolls everyone not yet enrolled.
        if (!classCourse.IsOptional)
        {
            await EnrollPlacedStudentsAsync(classCourse, cancellationToken);
        }
    }

    public async Task OnRemovingAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        var active = await db.Set<CourseEnrollment>()
            .Where(e => e.ClassCourseId == classCourse.Id && e.Status == CourseEnrollment.ActiveStatus)
            .ToListAsync(cancellationToken);
        if (active.Any(e => e.Source != EnrollmentSources.Auto))
        {
            throw new ClassCourseInUseException("enrollments");
        }

        if (await db.Set<GroupMember>().AnyAsync(m => m.ClassCourseId == classCourse.Id, cancellationToken))
        {
            throw new ClassCourseInUseException("groups");
        }

        await engine.DropAllAsync(active, "Course removed from the class", DateOnly.FromDateTime(clock.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnrollPlacedStudentsAsync(ClassCourse classCourse, CancellationToken ct)
    {
        var cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == classCourse.ClassId, ct);
        if (cls is null || cls.Status != Class.ActiveStatus)
        {
            return;
        }

        var placements = await db.Set<ClassStudent>()
            .Where(c => c.ClassId == cls.Id && c.Status == ClassStudent.ActiveStatus)
            .ToListAsync(ct);
        if (placements.Count == 0)
        {
            return;
        }

        var studentIds = placements.Select(p => p.StudentId).ToList();
        var alreadyEnrolled = (await db.Set<CourseEnrollment>()
                .Where(e => e.ClassCourseId == classCourse.Id && e.Status == CourseEnrollment.ActiveStatus)
                .Select(e => e.StudentId).ToListAsync(ct)).ToHashSet();
        var students = await db.Set<StudentProfile>()
            .Where(s => studentIds.Contains(s.Id) && s.Status == PersonStatus.Active).ToListAsync(ct);
        var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == cls.OrgUnitId, ct);
        if (unit is null)
        {
            return;
        }

        var restrictions = await guard.RestrictionsAsync(cls.OrganizationId, [classCourse.CourseId], ct);
        var rules = restrictions.GetValueOrDefault(classCourse.CourseId) ?? [];
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var created = new List<CourseEnrollment>();
        foreach (var student in students.Where(s => !alreadyEnrolled.Contains(s.Id)))
        {
            if (CourseRestrictionEvaluator.Failed(rules, student.Gender, student.Religion, unit.LevelTypeId).Count > 0)
            {
                continue; // restricted students are skipped; an exceptional enrollment can still admit them
            }

            created.Add(new CourseEnrollment(classCourse, placements.First(p => p.StudentId == student.Id), EnrollmentSources.Auto, today));
        }

        engine.AddEnrollments(created);
        await db.SaveChangesAsync(ct);
    }
}
