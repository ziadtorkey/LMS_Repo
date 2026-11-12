using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Classes;
using Lms.Shared.Teaching;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>Builds <see cref="ClassCourseDto"/>s with class and course names, enrolled counts and current teachers.</summary>
public sealed class ClassCourseMapper(LmsDbContext db, ClassLookups lookups, IClock clock)
{
    public async Task<IReadOnlyList<ClassCourseDto>> ToDtosAsync(IReadOnlyList<ClassCourse> classCourses, CancellationToken ct)
    {
        if (classCourses.Count == 0)
        {
            return [];
        }

        var organizationId = classCourses[0].OrganizationId; // one call = one client
        var ids = classCourses.Select(c => c.Id).ToList();
        var classIds = classCourses.Select(c => c.ClassId).Distinct().ToList();
        var courseIds = classCourses.Select(c => c.CourseId).Distinct().ToList();
        var today = DateOnly.FromDateTime(clock.UtcNow);

        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var courses = await lookups.CoursesAsync(organizationId, courseIds, ct);
        var enrolled = await db.Set<CourseEnrollment>()
            .Where(e => ids.Contains(e.ClassCourseId) && e.Status == CourseEnrollment.ActiveStatus)
            .GroupBy(e => e.ClassCourseId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var assignments = await db.Set<ClassCourseTeacher>()
            .Where(t => ids.Contains(t.ClassCourseId) && t.From <= today && (t.To == null || t.To >= today))
            .OrderBy(t => t.From).ToListAsync(ct);
        var userIds = assignments.Select(t => t.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        return classCourses.Select(cc =>
        {
            classes.TryGetValue(cc.ClassId, out var cls);
            courses.TryGetValue(cc.CourseId, out var course);
            var teachers = assignments.Where(t => t.ClassCourseId == cc.Id).Select(t =>
            {
                users.TryGetValue(t.UserId, out var user);
                return new ClassCourseTeacherDto(
                    t.Id, t.UserId, user?.DisplayNameAr ?? string.Empty, user?.DisplayNameEn ?? string.Empty, t.Role, t.From, t.To, true);
            }).ToList();
            return new ClassCourseDto(
                cc.Id, cc.ClassId, cls?.NameAr ?? string.Empty, cls?.NameEn ?? string.Empty, cc.CourseId, course?.Code ?? string.Empty,
                course?.NameAr ?? string.Empty, course?.NameEn ?? string.Empty, cc.IsOptional, enrolled.GetValueOrDefault(cc.Id), teachers, null);
        }).ToList();
    }

    public async Task<ClassCourseDto> ToDtoAsync(ClassCourse classCourse, CancellationToken ct) =>
        (await ToDtosAsync([classCourse], ct))[0];
}
