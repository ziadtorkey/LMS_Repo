using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Roles (Task 19, #96): client-wide. Reads at any scope; writes only at the client root (D18, Review Focus 4); a
/// platform admin has no client, so client-root writes are refused; roles of another client are invisible (404).
/// </summary>
public sealed class RolesProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("permissions catalog", HttpMethod.Get, Routes.Permissions,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Permissions)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("roles list", HttpMethod.Get, Routes.Roles,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Roles)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("roles create", HttpMethod.Post, Routes.Roles,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Roles, RoleBody(w))),
            Expect(Created, Forbidden, Forbidden, Forbidden, Created, Forbidden)),
        new("roles update (client A custom role)", HttpMethod.Put, Routes.RolePattern,
            async (w, _) => new ProbeRequest(Routes.Role(await NewRoleAsync(w)), RoleUpdateBody(w)),
            Expect(Ok, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
        new("roles delete (unused client A custom role)", HttpMethod.Delete, Routes.RolePattern,
            async (w, _) => new ProbeRequest(Routes.Role(await NewRoleAsync(w))),
            Expect(NoContent, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
    ];

    private static CreateRoleRequest RoleBody(M1ProbeWorld w)
    {
        var n = w.Next();
        return new CreateRoleRequest("دور " + n, "Probe role " + n, RoleBaseKinds.Staff, [Permissions.Roles.View]);
    }

    private static UpdateRoleRequest RoleUpdateBody(M1ProbeWorld w)
    {
        var n = w.Next();
        return new UpdateRoleRequest("دور " + n, "Probe role " + n, [Permissions.Roles.View]);
    }

    private static async Task<Guid> NewRoleAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Roles, RoleBody(w))).GetProperty("id").GetGuid();
}
