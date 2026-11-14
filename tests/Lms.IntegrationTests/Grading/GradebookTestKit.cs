using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Grading;

/// <summary>A class course whose year has two terms around a fixed window, so events can be scored in a known term.</summary>
internal static class GradebookTestKit
{
    public static readonly DateOnly YearStart = new(2043, 9, 1);

    public sealed record World(AcademicYear Year, Term First, Term Second, Class Class, ClassCourse ClassCourse);

    public static async Task<World> NewWorldAsync(LmsApiFactory factory, OrgUnit unit, DateOnly? yearStart = null)
    {
        var start = yearStart ?? YearStart;
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var year = new AcademicYear(unit, "عام", "Gradebook year " + unit.Code, start, start.AddMonths(10));
        db.Add(year);
        var first = new Term(year, "الأول", "First", start, start.AddMonths(4).AddDays(-1));
        var second = new Term(year, "الثاني", "Second", start.AddMonths(4), start.AddMonths(10));
        db.AddRange(first, second);
        first.SetOrder(1);
        second.SetOrder(2);
        await db.SaveChangesAsync();
        var course = await AcademicTestKit.AddCourseAsync(factory, unit);
        var cls = await AcademicTestKit.AddClassAsync(factory, unit, year);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        return new World(year, first, second, cls, classCourse);
    }

    public static DateTime At(DateOnly day) => day.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);

    public static async Task<Gradebook> GradebookAsync(LmsApiFactory factory, Guid classCourseId, Guid termId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Gradebook>().SingleAsync(g => g.ClassCourseId == classCourseId && g.TermId == termId);
    }
}
