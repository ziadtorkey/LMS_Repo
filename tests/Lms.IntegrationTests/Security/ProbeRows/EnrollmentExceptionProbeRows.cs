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
/// Exceptional enrollment (M3 Task 11, #219, D40). The class, its Muslims-only course and the restricted student are at School1; the
/// course has no supervisor, so the admins decide. A Teacher holds neither permission (403); School2 and other clients get 404; a
/// platform admin has no client context (list/decisions 404). Cancelling is for the requester only: the seeded request belongs to
/// nobody in the matrix, so it is 404 for every caller allowed to try.
/// </summary>
public sealed class EnrollmentExceptionProbeRows : IProbeRowModule
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("enrollment exception request", HttpMethod.Post, Routes.ClassCourseExceptionsPattern,
            async (w, _) =>
            {
                var (cc, studentId, _) = await NewWorldAsync(w, withException: false);
                return new ProbeRequest(Routes.ClassCourseExceptions(cc.Id), new RequestExceptionRequest(studentId, "probe"));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("enrollment exceptions list", HttpMethod.Get, Routes.EnrollmentExceptions,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.EnrollmentExceptions)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, NotFound)),
        new("enrollment exception get", HttpMethod.Get, Routes.EnrollmentExceptionPattern,
            async (w, _) => new ProbeRequest(Routes.EnrollmentException((await NewWorldAsync(w)).ExceptionId)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("enrollment exception approve", HttpMethod.Post, Routes.EnrollmentExceptionApprovePattern,
            async (w, _) => new ProbeRequest(Routes.EnrollmentExceptionApprove((await NewWorldAsync(w)).ExceptionId), new DecideExceptionRequest("ok")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("enrollment exception reject", HttpMethod.Post, Routes.EnrollmentExceptionRejectPattern,
            async (w, _) => new ProbeRequest(Routes.EnrollmentExceptionReject((await NewWorldAsync(w)).ExceptionId), new DecideExceptionRequest("no")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("enrollment exception cancel (not the requester)", HttpMethod.Post, Routes.EnrollmentExceptionCancelPattern,
            async (w, _) => new ProbeRequest(Routes.EnrollmentExceptionCancel((await NewWorldAsync(w)).ExceptionId)),
            Expect(NotFound, NotFound, NotFound, Forbidden, NotFound, NotFound)),
    ];

    private static async Task<(ClassCourse ClassCourse, Guid StudentId, Guid ExceptionId)> NewWorldAsync(M1ProbeWorld w, bool withException = true)
    {
        var factory = w.Callers.Factory;
        var unit = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, unit);
        var cls = await AcademicTestKit.AddClassAsync(factory, unit, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, unit);
        var student = await PeopleTestKit.AddStudentAsync(factory, unit, PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<Lms.Domain.People.StudentProfile>().SingleAsync(s => s.Id == student.Id);
        tracked.Update(tracked.NameAr, tracked.NameEn, null, null, tracked.DateOfBirth, tracked.Gender, "christian", tracked.Nationality, null, null, null, null);
        var classCourse = new ClassCourse(cls, course, true);
        db.AddRange(
            new CourseRestriction(course, CourseRestrictionEvaluator.Religion, ["muslim"]), new ClassStudent(cls, student.Id, Today), classCourse);
        await db.SaveChangesAsync();
        if (!withException)
        {
            return (classCourse, student.Id, Guid.Empty);
        }

        var exception = new EnrollmentException(classCourse, student.Id, "[\"Religion\"]", "probe", Guid.NewGuid());
        db.Add(exception);
        await db.SaveChangesAsync();
        return (classCourse, student.Id, exception.Id);
    }
}
