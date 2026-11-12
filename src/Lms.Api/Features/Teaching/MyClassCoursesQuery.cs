using Lms.Api.Features.Classes;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Teaching;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Teaching;

/// <summary>
/// GET /api/me/class-courses: the class courses the signed-in user teaches today and, as a student, is enrolled in.
/// It lists the caller's own relationships whatever their role.
/// </summary>
public sealed class MyClassCoursesQuery(LmsDbContext db, ICurrentContext context, IClock clock, ClassLookups lookups)
{
    public async Task<IResult> GetAsync(Guid? yearId, CancellationToken ct)
    {
        if (context.UserId is not { } userId)
        {
            return Results.Ok(Array.Empty<MyClassCourseDto>());
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var teaching = await db.Set<ClassCourseTeacher>()
            .Where(t => t.UserId == userId && t.From <= today && (t.To == null || t.To >= today))
            .Select(t => new { t.ClassCourseId, t.Role }).ToListAsync(ct);
        var enrolled = await db.Set<CourseEnrollment>()
            .Where(e => e.Status == CourseEnrollment.ActiveStatus
                && db.Set<StudentProfile>().Any(s => s.Id == e.StudentId && s.UserId == userId))
            .Select(e => e.ClassCourseId).ToListAsync(ct);
        var roles = teaching.GroupBy(t => t.ClassCourseId)
            .ToDictionary(g => g.Key, g => g.Any(x => x.Role == TeacherRoles.Primary) ? TeacherRoles.Primary : g.First().Role);
        var ids = roles.Keys.Concat(enrolled).Distinct().ToList();

        var classCourses = await db.Set<ClassCourse>().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var classIds = classCourses.Select(c => c.ClassId).Distinct().ToList();
        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id) && (yearId == null || c.AcademicYearId == yearId))
            .ToDictionaryAsync(c => c.Id, ct);
        classCourses = classCourses.Where(c => classes.ContainsKey(c.ClassId)).ToList();
        if (classCourses.Count == 0)
        {
            return Results.Ok(Array.Empty<MyClassCourseDto>());
        }

        var organizationId = classCourses[0].OrganizationId;
        var courses = await lookups.CoursesAsync(organizationId, classCourses.Select(c => c.CourseId).Distinct().ToList(), ct);
        var unitIds = classes.Values.Select(c => c.OrgUnitId).Distinct().ToList();
        var units = await db.OrgUnits.Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var counts = await db.Set<CourseEnrollment>()
            .Where(e => ids.Contains(e.ClassCourseId) && e.Status == CourseEnrollment.ActiveStatus)
            .GroupBy(e => e.ClassCourseId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);

        var rows = classCourses.OrderBy(c => classes[c.ClassId].Code).ThenBy(c => c.CourseId).Select(cc =>
        {
            var cls = classes[cc.ClassId];
            courses.TryGetValue(cc.CourseId, out var course);
            units.TryGetValue(cls.OrgUnitId, out var unit);
            return new MyClassCourseDto(
                cc.Id, cls.Id, cls.NameAr, cls.NameEn, course?.NameAr ?? string.Empty, course?.NameEn ?? string.Empty,
                unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty, roles.GetValueOrDefault(cc.Id), counts.GetValueOrDefault(cc.Id));
        }).ToList();
        return Results.Ok(rows);
    }
}
