using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>Builds <see cref="ClassSummaryDto"/>s with unit, year and homeroom names and the student and course counts.</summary>
public sealed class ClassMapper(LmsDbContext db, ClassLookups lookups)
{
    public async Task<IReadOnlyList<ClassSummaryDto>> ToDtosAsync(IReadOnlyList<Class> classes, CancellationToken ct)
    {
        if (classes.Count == 0)
        {
            return [];
        }

        var organizationId = classes[0].OrganizationId; // one call = one client
        var classIds = classes.Select(c => c.Id).ToList();
        var unitIds = classes.Select(c => c.OrgUnitId).Distinct().ToList();
        var units = await db.OrgUnits.Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NameAr, u.NameEn }).ToDictionaryAsync(u => u.Id, ct);
        var years = await lookups.YearsAsync(organizationId, classes.Select(c => c.AcademicYearId).Distinct().ToList(), ct);
        var teacherIds = classes.Where(c => c.HomeroomTeacherUserId is not null).Select(c => c.HomeroomTeacherUserId!.Value).Distinct().ToList();
        var teachers = await db.Users.Where(u => teacherIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var students = await db.Set<ClassStudent>().Where(s => classIds.Contains(s.ClassId) && s.Status == ClassStudent.ActiveStatus)
            .GroupBy(s => s.ClassId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var courses = await db.Set<ClassCourse>().Where(c => classIds.Contains(c.ClassId))
            .GroupBy(c => c.ClassId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);

        return classes.Select(c =>
        {
            units.TryGetValue(c.OrgUnitId, out var unit);
            years.TryGetValue(c.AcademicYearId, out var year);
            var teacher = c.HomeroomTeacherUserId is { } id && teachers.TryGetValue(id, out var t) ? t : null;
            return new ClassSummaryDto(
                c.Id, c.Code, c.NameAr, c.NameEn, c.OrgUnitId, unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty,
                c.AcademicYearId, year?.NameAr ?? string.Empty, year?.NameEn ?? string.Empty, c.Capacity,
                students.GetValueOrDefault(c.Id), courses.GetValueOrDefault(c.Id), c.HomeroomTeacherUserId,
                teacher?.DisplayNameAr, teacher?.DisplayNameEn, c.Status,
                c.Status == Class.ClosedStatus || year?.Status == AcademicYearStatus.Closed);
        }).ToList();
    }

    public async Task<ClassSummaryDto> ToDtoAsync(Class cls, CancellationToken ct) => (await ToDtosAsync([cls], ct))[0];
}
