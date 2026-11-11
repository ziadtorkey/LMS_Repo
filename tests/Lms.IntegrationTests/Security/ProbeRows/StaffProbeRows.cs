using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Staff (M2 Task 10): created and read at a unit. Staff homed in School1 are invisible to School2's admin (404) and
/// to client B; a Teacher holds no Staff permission at all.
/// </summary>
public sealed class StaffProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("staff list", HttpMethod.Get, Routes.Staff,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Staff)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, NotFound)),
        new("staff create (home School1)", HttpMethod.Post, Routes.Staff,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Staff, StaffBody(w))),
            Expect(Created, Created, NotFound, Forbidden, NotFound, NotFound)),
        new("staff profile (homed at School1)", HttpMethod.Get, Routes.StaffPattern,
            async (w, _) => new ProbeRequest(Routes.StaffMember(await NewStaffAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
    ];

    private static CreateStaffRequest StaffBody(M1ProbeWorld w)
    {
        var n = w.Next();
        return new CreateStaffRequest(
            "موظف " + n, "Probe staff " + n, $"probe-staff-{n}-{Guid.NewGuid():N}@lms.test", null, null, null, null, null,
            w.Callers.A.School1.Id, null,
            [new StaffRoleInput(w.Callers.A.Roles[BuiltInRoles.Teacher].Id, w.Callers.A.School1.Id, null, null)]);
    }

    private static async Task<Guid> NewStaffAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Staff, StaffBody(w)))
            .GetProperty("summary").GetProperty("id").GetGuid();
}
