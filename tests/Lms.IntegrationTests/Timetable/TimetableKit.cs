using System.Net.Http.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Timetable;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Timetable;

/// <summary>One class with a term, two periods, two class courses and a teacher for each, all at one unit.</summary>
internal sealed record TimetableWorld(
    OrgUnit Unit, AcademicYear Year, Term Term, Class Class, Period First, Period Second,
    ClassCourse Math, Guid MathTeacher, ClassCourse Science, Guid ScienceTeacher);

internal static class TimetableKit
{
    public static async Task<TimetableWorld> NewWorldAsync(LmsApiFactory factory, OrgUnit unit)
    {
        var year = await AcademicTestKit.AddYearAsync(factory, unit);
        Term term;
        Period first, second;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            term = new Term(year, "فصل", "Term 1", year.StartDate, year.StartDate.AddMonths(4));
            first = new Period(unit, "الأولى", "First", new TimeOnly(8, 0), new TimeOnly(8, 45), 1);
            second = new Period(unit, "الثانية", "Second", new TimeOnly(9, 0), new TimeOnly(9, 45), 2);
            db.AddRange(term, first, second);
            await db.SaveChangesAsync();
        }

        var cls = await AcademicTestKit.AddClassAsync(factory, unit, year);
        var (math, mathTeacher) = await AddCourseWithTeacherAsync(factory, unit, cls);
        var (science, scienceTeacher) = await AddCourseWithTeacherAsync(factory, unit, cls);
        return new TimetableWorld(unit, year, term, cls, first, second, math, mathTeacher, science, scienceTeacher);
    }

    public static async Task<(ClassCourse ClassCourse, Guid TeacherId)> AddCourseWithTeacherAsync(
        LmsApiFactory factory, OrgUnit unit, Class cls, Guid? teacherId = null)
    {
        var course = await AcademicTestKit.AddCourseAsync(factory, unit);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        var teacher = teacherId ?? (await PeopleTestKit.AddUserAsync(factory)).Id;
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new ClassCourseTeacher(classCourse, teacher, ClassCourseTeacher.PrimaryRole, new DateOnly(2041, 9, 1)));
        await db.SaveChangesAsync();
        return (classCourse, teacher);
    }

    public static SaveSlotRequest Slot(TimetableWorld w, ClassCourse cc, Guid teacher, DayOfWeek day, Period period, string? room = null, int? version = null) =>
        new(cc.Id, w.Term.Id, teacher, day, period.Id, room, version);

    public static async Task<SaveSlotResultDto> CreateSlotAsync(LmsApiFactory factory, RoleAssignment who, Guid classId, SaveSlotRequest body)
    {
        var response = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableSlots(classId), who, body);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SaveSlotResultDto>(Web))!;
    }
}
