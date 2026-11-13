using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Guardian portal (M3 Task 25, #233): <c>POST /api/guardians/{id}/portal-invitation</c> needs Students.Update (admins and the
/// counselor, not a Teacher) and a guardian visible through a child in the caller's scope; a platform admin has no client.
/// The probe guardian has no email, so the answer for an allowed caller is 400 and nothing is created or sent.
/// <c>GET /api/me/children</c> is self-scoped (the caller's own guardian profile) and has no probe row.
/// </summary>
public sealed class GuardianPortalProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("guardian portal invitation (child in School1)", HttpMethod.Post, Routes.GuardianPortalInvitationPattern,
            async (w, _) => new ProbeRequest(Routes.GuardianPortalInvitation(await NewGuardianAsync(w))),
            Expect(BadRequest, BadRequest, NotFound, Forbidden, NotFound, NotFound)),
    ];

    private static async Task<Guid> NewGuardianAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var student = await PeopleTestKit.AddStudentAsync(factory, w.Callers.A.School1, PeopleTestKit.NewCode());
        var guardian = await PeopleTestKit.AddGuardianAsync(factory, w.Callers.A.Org.Id, PeopleTestKit.NewMobile());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<StudentProfile>().FindAsync(student.Id);
        var profile = await db.Set<GuardianProfile>().FindAsync(guardian.Id);
        db.Add(StudentGuardian.For(tracked!, profile!, "Father", true));
        await db.SaveChangesAsync();
        return guardian.Id;
    }
}
