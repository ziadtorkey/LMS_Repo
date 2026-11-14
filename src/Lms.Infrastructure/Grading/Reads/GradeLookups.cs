using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Reads;

/// <summary>Names of things a grade read shows but the reader's unit scope may not reach (a course or term defined at an ancestor unit).</summary>
public sealed class GradeLookups(LmsDbContext db)
{
    /// <summary>The course of a class course, names only leave.</summary>
    public async Task<(string NameAr, string NameEn)> CourseNameAsync(ClassCourse classCourse, CancellationToken ct)
    {
        // ISOLATION-BYPASS: an inherited course is owned above the reader's subtree; limited to the client, the live row and the one course
        // the reader already reaches through their class course; only the names leave.
        var course = await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.OrganizationId == classCourse.OrganizationId && c.Id == classCourse.CourseId)
            .Select(c => new { c.NameAr, c.NameEn }).FirstOrDefaultAsync(ct);
        return course is null ? (string.Empty, string.Empty) : (course.NameAr, course.NameEn);
    }

    /// <summary>A term of the class course's own year (terms live at the year's unit, above a student or teacher).</summary>
    public async Task<Term?> TermOfClassCourseAsync(ClassCourse classCourse, Guid termId, CancellationToken ct)
    {
        var yearId = await db.Set<Class>().Where(c => c.Id == classCourse.ClassId).Select(c => c.AcademicYearId).SingleAsync(ct);
        // ISOLATION-BYPASS: terms are defined at the year's unit above the reader; limited to the client, the live row, the year of the
        // reader's class course and the one term id.
        return await db.Set<Term>().IgnoreQueryFilters()
            .Where(t => !t.IsDeleted && t.OrganizationId == classCourse.OrganizationId && t.AcademicYearId == yearId && t.Id == termId)
            .FirstOrDefaultAsync(ct);
    }
}
