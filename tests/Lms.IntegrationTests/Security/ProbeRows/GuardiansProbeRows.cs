using Lms.Shared;
using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.People;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Guardians (M3 Task 19, #227): the student sits in School1. Students.Update (linking, editing, matching) is held by the
/// admins and the counselor, not by a Teacher or Supervisor; Students.View is also held by a Teacher, narrowed by the relationship
/// rule (D42) to the students of their own class courses or homeroom class: the probe's student is none of those, so the
/// Teacher gets 404 for the student's guardians and for a guardian of that student (the list is 200 and empty).
/// </summary>
public sealed class GuardiansProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("student guardians list", HttpMethod.Get, Routes.StudentGuardiansPattern,
            async (w, _) => new ProbeRequest(Routes.StudentGuardians((await NewLinkAsync(w)).StudentId)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, NotFound)),
        new("student guardians link", HttpMethod.Post, Routes.StudentGuardiansPattern,
            async (w, _) => new ProbeRequest(Routes.StudentGuardians((await NewStudentAsync(w)).Id), NewGuardianBody()),
            Expect(Created, Created, NotFound, Forbidden, NotFound, NotFound)),
        new("student guardians update", HttpMethod.Put, Routes.StudentGuardianPattern,
            async (w, _) =>
            {
                var link = await NewLinkAsync(w);
                return new ProbeRequest(Routes.StudentGuardian(link.StudentId, link.GuardianId), new UpdateStudentGuardianRequest("Mother", true));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("student guardians unlink", HttpMethod.Delete, Routes.StudentGuardianPattern,
            async (w, _) =>
            {
                var link = await NewLinkAsync(w);
                return new ProbeRequest(Routes.StudentGuardian(link.StudentId, link.GuardianId));
            },
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NotFound)),
        new("guardians list", HttpMethod.Get, Routes.Guardians,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Guardians)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
        new("guardians get (child in School1)", HttpMethod.Get, Routes.GuardianPattern,
            async (w, _) => new ProbeRequest(Routes.Guardian((await NewLinkAsync(w)).GuardianId)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, NotFound)),
        new("guardians edit (child in School1)", HttpMethod.Put, Routes.GuardianPattern,
            async (w, _) =>
            {
                var link = await NewLinkAsync(w);
                return new ProbeRequest(Routes.Guardian(link.GuardianId),
                    new UpdateGuardianRequest(new GuardianDataInput("ولي", "Probe edit", link.Mobile, null, null)));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("guardians match", HttpMethod.Get, Routes.GuardiansMatch,
            async (w, _) => new ProbeRequest($"{Routes.GuardiansMatch}?nationalId={(await NewLinkAsync(w)).NationalId}"),
            Expect(Ok, Ok, Ok, Forbidden, NoContent, NoContent)),
    ];

    private static GuardianLinkInput NewGuardianBody()
    {
        var tag = Guid.NewGuid().ToString("N");
        return new GuardianLinkInput(null,
            new GuardianDataInput("ولي أمر", "Probe guardian " + tag[..6], "+2010" + Random.Shared.Next(10000000, 99999999), null, "P" + tag[..10].ToUpperInvariant()),
            "Father", true);
    }

    private static async Task<(Guid StudentId, Guid GuardianId, string Mobile, string NationalId)> NewLinkAsync(M1ProbeWorld w)
    {
        var student = await NewStudentAsync(w);
        var body = NewGuardianBody();
        var link = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.StudentGuardians(student.Id), body);
        return (student.Id, link.GetProperty("guardianId").GetGuid(), body.NewGuardian!.Mobile, body.NewGuardian.NationalId!);
    }

    /// <summary>A student seeded directly in School1 of client A (the student endpoints come with a later task).</summary>
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
