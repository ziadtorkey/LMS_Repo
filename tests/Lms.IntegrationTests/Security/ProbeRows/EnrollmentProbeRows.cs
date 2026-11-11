using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Class placement and course enrollment (M3 Task 24, #232). Everything is checked at the unit of the class (School1):
/// School 2 and the other client get 404, the teacher has no Enrollment.Manage (403) and, for reads, no teaching
/// relationship to the probe's class courses (404). The list endpoints are scope-filtered and answer 200 for every client caller.
/// </summary>
public sealed class EnrollmentProbeRows : IProbeRowModule
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("class students add", HttpMethod.Post, Routes.ClassStudentsPattern,
            async (w, _) =>
            {
                var cls = await NewClassAsync(w);
                var student = await PeopleTestKit.AddStudentAsync(w.Callers.Factory, w.Callers.A.School1, PeopleTestKit.NewCode());
                return new ProbeRequest(Routes.ClassStudents(cls.Id), new AddStudentsToClassRequest([student.Id], null, false));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("class students list", HttpMethod.Get, Routes.ClassStudentsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassStudents((await NewClassAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("class students remove", HttpMethod.Post, Routes.ClassStudentRemovePattern,
            async (w, _) =>
            {
                var (cls, placement, _) = await NewPlacementAsync(w);
                return new ProbeRequest(Routes.ClassStudentRemove(cls.Id, placement.Id), new RemoveFromClassRequest(Today, "probe"));
            },
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
        new("class course enrollments add", HttpMethod.Post, Routes.ClassCourseEnrollmentsPattern,
            async (w, _) =>
            {
                var (_, placement, optional) = await NewPlacementAsync(w);
                return new ProbeRequest(Routes.ClassCourseEnrollments(optional.Id), new EnrollStudentsRequest([placement.StudentId]));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("class course enrollments list", HttpMethod.Get, Routes.ClassCourseEnrollmentsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseEnrollments((await NewPlacementAsync(w)).Optional.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("enrollment drop", HttpMethod.Post, Routes.EnrollmentDropPattern,
            async (w, _) => new ProbeRequest(Routes.EnrollmentDrop(await NewEnrollmentAsync(w)), new DropEnrollmentRequest("probe", null)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
        new("student enrollments list", HttpMethod.Get, Routes.StudentEnrollmentsPattern,
            async (w, _) =>
            {
                var (_, placement, _) = await NewPlacementAsync(w);
                return new ProbeRequest(Routes.StudentEnrollments(placement.StudentId));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("enrollments query", HttpMethod.Get, Routes.Enrollments,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Enrollments)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
    ];

    private static async Task<Class> NewClassAsync(M1ProbeWorld w)
    {
        var year = await AcademicTestKit.AddYearAsync(w.Callers.Factory, w.Callers.A.School1);
        return await AcademicTestKit.AddClassAsync(w.Callers.Factory, w.Callers.A.School1, year);
    }

    /// <summary>A class at School1 with a placed student and an optional class course (no enrollment in it yet).</summary>
    private static async Task<(Class Class, ClassStudent Placement, ClassCourse Optional)> NewPlacementAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var cls = await NewClassAsync(w);
        var student = await PeopleTestKit.AddStudentAsync(factory, w.Callers.A.School1, PeopleTestKit.NewCode());
        var course = await AcademicTestKit.AddCourseAsync(factory, w.Callers.A.School1);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var placement = new ClassStudent(cls, student.Id, Today);
        var optional = new ClassCourse(cls, course, true);
        db.AddRange(placement, optional);
        await db.SaveChangesAsync();
        return (cls, placement, optional);
    }

    private static async Task<Guid> NewEnrollmentAsync(M1ProbeWorld w)
    {
        var (_, placement, optional) = await NewPlacementAsync(w);
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var enrollment = new CourseEnrollment(optional, placement, EnrollmentSources.Manual, Today);
        db.Add(enrollment);
        await db.SaveChangesAsync();
        return enrollment.Id;
    }
}
