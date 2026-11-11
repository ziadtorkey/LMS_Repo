using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Staff lifecycle (M2 Task 11, #163): edit, status, delete and resend invitation of a person homed in School1.
/// School1's admin may, School2's admin and client B cannot see the person (404), a Teacher holds no Staff permission.
/// </summary>
public sealed class StaffLifecycleProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("staff edit (homed at School1)", HttpMethod.Put, Routes.StaffPattern,
            async (w, _) =>
            {
                var staff = await NewStaffAsync(w);
                return new ProbeRequest(Routes.StaffMember(staff),
                    new UpdateStaffRequest("اسم", "Edited", null, null, null, null, null, w.Callers.A.School1.Id, null));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("staff status (homed at School1)", HttpMethod.Put, Routes.StaffStatusPattern,
            async (w, _) => new ProbeRequest(Routes.StaffStatus(await NewStaffAsync(w)),
                new SetPersonStatusRequest(PersonStatuses.Deactivated, "probe")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("staff delete (homed at School1)", HttpMethod.Delete, Routes.StaffPattern,
            async (w, _) => new ProbeRequest(Routes.StaffMember(await NewStaffAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NotFound)),
        new("staff resend invitation (homed at School1)", HttpMethod.Post, Routes.StaffResendInvitationPattern,
            async (w, _) => new ProbeRequest(Routes.StaffResendInvitation(await NewStaffAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NotFound)),
    ];

    private static async Task<Guid> NewStaffAsync(M1ProbeWorld w)
    {
        var n = w.Next();
        var body = new CreateStaffRequest(
            "موظف " + n, "Probe lifecycle " + n, $"probe-life-{n}-{Guid.NewGuid():N}@lms.test", null, null, null, null, null,
            w.Callers.A.School1.Id, null,
            [new StaffRoleInput(w.Callers.A.Roles[BuiltInRoles.Counselor].Id, w.Callers.A.School1.Id, null, null)]);
        return (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Staff, body))
            .GetProperty("summary").GetProperty("id").GetGuid();
    }
}
