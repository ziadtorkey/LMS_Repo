using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.People;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Students (M3 Task 21, #229): the student is homed in School1. Writes need Students.Create/Update/Delete (admins, not a Teacher).
/// A Teacher holds Students.View but only for students of own classes (D32): the list answers 200 (empty), a single student 404.
/// </summary>
public sealed class StudentsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("students list", HttpMethod.Get, Routes.Students,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Students)),
            Expect(Ok, Ok, Ok, Ok, Ok, NotFound)),
        new("students create (home School1)", HttpMethod.Post, Routes.Students,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Students,
                new CreateStudentRequest(Data(w), w.Callers.A.School1.Id, null, true, []))),
            Expect(Created, Created, NotFound, Forbidden, NotFound, NotFound)),
        new("students get (homed at School1)", HttpMethod.Get, Routes.StudentPattern,
            async (w, _) => new ProbeRequest(Routes.Student((await NewStudentAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, NotFound)),
        new("students edit", HttpMethod.Put, Routes.StudentPattern,
            async (w, _) =>
            {
                var s = await NewStudentAsync(w);
                return new ProbeRequest(Routes.Student(s.Id), new UpdateStudentRequest(Data(w), w.Callers.A.School1.Id, s.StudentCode, true));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("students status", HttpMethod.Put, Routes.StudentStatusPattern,
            async (w, _) => new ProbeRequest(Routes.StudentStatus((await NewStudentAsync(w)).Id),
                new SetPersonStatusRequest(PersonStatuses.Deactivated, "probe")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("students delete", HttpMethod.Delete, Routes.StudentPattern,
            async (w, _) => new ProbeRequest(Routes.Student((await NewStudentAsync(w)).Id)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NotFound)),
        new("students feature flags", HttpMethod.Put, Routes.StudentFeatureFlagsPattern,
            async (w, _) => new ProbeRequest(Routes.StudentFeatureFlags((await NewStudentAsync(w)).Id),
                new SetStudentFeatureFlagsRequest(new Dictionary<string, bool?>())),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("students reset password", HttpMethod.Post, Routes.StudentResetPasswordPattern,
            async (w, _) => new ProbeRequest(Routes.StudentResetPassword((await NewStudentAsync(w)).Id), new ResetStudentPasswordRequest(null)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
    ];

    private static StudentDataInput Data(M1ProbeWorld w) => new(
        "طالب", "Probe student " + w.Next() + " " + Guid.NewGuid().ToString("N")[..6], null, null, new DateOnly(2014, 3, 4), "Male", "Muslim", "EG",
        null, null, null, null);

    /// <summary>A student seeded directly in School1 of client A with an account (the reset route needs none, the others ignore it).</summary>
    private static async Task<StudentProfile> NewStudentAsync(M1ProbeWorld w)
    {
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var student = new StudentProfile(
            w.Callers.A.School1, "P" + Guid.NewGuid().ToString("N")[..10], "طالب", "Probe student " + w.Next(),
            new DateOnly(2014, 5, 1), "Male", "Muslim", "EG");
        db.Set<StudentProfile>().Add(student);
        await db.SaveChangesAsync();
        return student;
    }
}
