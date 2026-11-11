using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Staff sharing (M2 Task 16, #164): the person is homed in School1 and shared into School2. Sharing needs the owner
/// (home) and the target unit in scope, so only the client root can do both here; the admin of the shared-to unit can
/// remove a share that points at their unit even though the person's home is outside their scope.
/// </summary>
public sealed class StaffSharesProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("staff share (home School1 to School2)", HttpMethod.Post, Routes.StaffSharesPattern,
            async (w, _) =>
            {
                var staff = await NewStaffAsync(w);
                return new ProbeRequest(Routes.StaffShares(staff), ShareBody(w));
            },
            Expect(Ok, NotFound, NotFound, Forbidden, NotFound, NotFound)),
        new("staff unshare (share at School2)", HttpMethod.Delete, Routes.StaffSharePattern,
            async (w, _) =>
            {
                var staff = await NewStaffAsync(w);
                var share = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.StaffShares(staff), ShareBody(w));
                return new ProbeRequest(Routes.StaffShare(staff, share[0].GetProperty("assignmentId").GetGuid()));
            },
            Expect(NoContent, NotFound, NoContent, Forbidden, NotFound, NotFound)),
    ];

    private static ShareStaffRequest ShareBody(M1ProbeWorld w) =>
        new([new StaffRoleInput(w.Callers.A.Roles[BuiltInRoles.Teacher].Id, w.Callers.A.School2.Id, null, null)]);

    private static async Task<Guid> NewStaffAsync(M1ProbeWorld w)
    {
        var n = w.Next();
        var body = new CreateStaffRequest(
            "موظف " + n, "Probe shared " + n, $"probe-share-{n}-{Guid.NewGuid():N}@lms.test", null, null, null, null, null,
            w.Callers.A.School1.Id, null,
            [new StaffRoleInput(w.Callers.A.Roles[BuiltInRoles.Counselor].Id, w.Callers.A.School1.Id, null, null)]);
        return (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Staff, body))
            .GetProperty("summary").GetProperty("id").GetGuid();
    }
}
