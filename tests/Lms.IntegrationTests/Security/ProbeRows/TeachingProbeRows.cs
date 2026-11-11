using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Teaching;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Teacher eligibility and class-course teachers (M3 Task 15): managed at the class unit by those holding
/// TeacherAssignments.Manage; a Teacher may read their own class courses only (a class course they do not teach is
/// 404) and manages nothing (403); School2 and client B cannot see School1 data (404).
/// </summary>
public sealed class TeachingProbeRows : IProbeRowModule
{
    // Dates are inside the year the academic test kit creates (starts 2041-09-01).
    private static readonly DateOnly Day = new(2041, 9, 5);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("teacher eligibility grant (School1)", HttpMethod.Post, Routes.TeacherEligibility,
            async (w, _) =>
            {
                var (teacher, course) = (await TeacherAsync(w), await CourseAsync(w));
                return new ProbeRequest(Routes.TeacherEligibility, new GrantEligibilityRequest(w.Callers.A.School1.Id, [teacher], [course]));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("teacher eligibility list (School1)", HttpMethod.Get, Routes.TeacherEligibility,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.TeacherEligibility + $"?orgUnitId={w.Callers.A.School1.Id}")),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("teacher eligibility delete (School1)", HttpMethod.Delete, Routes.TeacherEligibilityItemPattern,
            async (w, _) =>
            {
                var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.TeacherEligibility,
                    new GrantEligibilityRequest(w.Callers.A.School1.Id, [await TeacherAsync(w)], [await CourseAsync(w)]));
                return new ProbeRequest(Routes.TeacherEligibilityItem(created[0].GetProperty("id").GetGuid()));
            },
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
        new("class course teachers list (School1)", HttpMethod.Get, Routes.ClassCourseTeachersPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseTeachers((await ClassCourseAsync(w)).ClassCourse)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("class course teacher assign (School1)", HttpMethod.Post, Routes.ClassCourseTeachersPattern,
            async (w, _) =>
            {
                var s = await ClassCourseAsync(w);
                return new ProbeRequest(Routes.ClassCourseTeachers(s.ClassCourse), new AssignTeacherRequest(s.Teacher, TeacherRoles.Primary, Day));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("class course teacher replace (School1)", HttpMethod.Post, Routes.ClassCourseTeacherReplacePattern,
            async (w, _) =>
            {
                var s = await ClassCourseAsync(w);
                var row = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseTeachers(s.ClassCourse),
                    new AssignTeacherRequest(s.Teacher, TeacherRoles.Primary, Day));
                var next = await TeacherAsync(w, s.Course);
                return new ProbeRequest(Routes.ClassCourseTeacherReplace(s.ClassCourse, row.GetProperty("id").GetGuid()),
                    new ReplaceTeacherRequest(next, Day.AddDays(3)));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("class course teacher end (School1)", HttpMethod.Post, Routes.ClassCourseTeacherEndPattern,
            async (w, _) =>
            {
                var s = await ClassCourseAsync(w);
                var row = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseTeachers(s.ClassCourse),
                    new AssignTeacherRequest(s.Teacher, TeacherRoles.Primary, Day));
                return new ProbeRequest(Routes.ClassCourseTeacherEnd(s.ClassCourse, row.GetProperty("id").GetGuid()), new EndTeacherRequest(Day.AddDays(2)));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("teacher assignments bulk (School1)", HttpMethod.Post, Routes.TeacherAssignmentsBulk,
            async (w, _) =>
            {
                var s = await ClassCourseAsync(w);
                return new ProbeRequest(Routes.TeacherAssignmentsBulk, new BulkAssignTeacherRequest(s.Teacher, TeacherRoles.Assistant, [s.ClassCourse]));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("my class courses", HttpMethod.Get, Routes.MyClassCourses,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyClassCourses)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
    ];

    private sealed record Setup(Guid ClassCourse, Guid Teacher, Guid Course);

    private static async Task<Guid> CourseAsync(M1ProbeWorld w)
    {
        var course = await AcademicTestKit.AddCourseAsync(w.Callers.Factory, w.Callers.A.Root);
        await AcademicTestKit.AllocateAsync(w.Callers.Factory, course, w.Callers.A.Root);
        return course.Id;
    }

    private static async Task<Guid> TeacherAsync(M1ProbeWorld w, Guid? eligibleFor = null)
    {
        var user = await PeopleTestKit.AddUserAsync(w.Callers.Factory);
        await AccessTestData.AssignAsync(w.Callers.Factory, w.Callers.A, w.Callers.A.School1, BuiltInRoles.Teacher, user.Id);
        if (eligibleFor is { } course)
        {
            await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.TeacherEligibility,
                new GrantEligibilityRequest(w.Callers.A.School1.Id, [user.Id], [course]));
        }

        return user.Id;
    }

    /// <summary>A class course at School1 and an eligible teacher of it.</summary>
    private static async Task<Setup> ClassCourseAsync(M1ProbeWorld w)
    {
        var year = await AcademicTestKit.AddYearAsync(w.Callers.Factory, w.Callers.A.Root);
        var course = await CourseAsync(w);
        var cls = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Classes,
            new SaveClassRequest("TP" + w.Next() + Guid.NewGuid().ToString("N")[..6], "فصل", "Probe", w.Callers.A.School1.Id, year.Id, 20, null));
        var added = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCoursesOfClass(cls.GetProperty("id").GetGuid()),
            new AddClassCoursesRequest([course], false));
        return new Setup(added[0].GetProperty("id").GetGuid(), await TeacherAsync(w, course), course);
    }
}
