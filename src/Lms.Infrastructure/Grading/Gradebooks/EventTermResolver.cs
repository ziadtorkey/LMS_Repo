using Lms.Domain.AcademicYears;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>
/// Which term a grade event belongs to (D58 Q8): the term of the class course year that contains the day the event was scored; when that day
/// falls between terms or after the last one, the term that is current today, else the last term of the year. Null when the year has no
/// terms yet (no gradebook can exist; the backfill picks the event up once terms exist).
/// </summary>
public sealed class EventTermResolver(LmsDbContext db, IClock clock)
{
    public async Task<Term?> ResolveAsync(ClassCourse classCourse, DateTime scoredAtUtc, CancellationToken ct)
    {
        var yearId = await db.Set<Class>().Where(c => c.Id == classCourse.ClassId).Select(c => c.AcademicYearId).SingleAsync(ct);

        // ISOLATION-BYPASS: the terms of a class year are defined at an ancestor unit above the caller scope (a teacher sees only the class
        // unit); the client is matched explicitly, deleted terms are excluded by hand and only terms of that one year are read.
        var terms = await db.Set<Term>().IgnoreQueryFilters()
            .Where(t => !t.IsDeleted && t.OrganizationId == classCourse.OrganizationId && t.AcademicYearId == yearId)
            .OrderBy(t => t.StartDate).ToListAsync(ct);
        if (terms.Count == 0)
        {
            return null;
        }

        var day = DateOnly.FromDateTime(scoredAtUtc);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return terms.FirstOrDefault(t => t.StartDate <= day && day <= t.EndDate)
               ?? terms.FirstOrDefault(t => t.StartDate <= today && today <= t.EndDate)
               ?? terms[^1];
    }
}
