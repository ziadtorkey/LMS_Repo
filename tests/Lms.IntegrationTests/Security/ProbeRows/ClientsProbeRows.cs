using Lms.Shared;
using Lms.Shared.Organizations;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Clients (Task 7, #92): Platform Admin only (platform-only permissions); everyone else 403, anonymous 401.</summary>
public sealed class ClientsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("clients list", HttpMethod.Get, Routes.Clients,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Clients)),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok)),
        new("clients get", HttpMethod.Get, Routes.ClientPattern,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Client(w.Callers.A.Org.Id))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok)),
        new("clients create", HttpMethod.Post, Routes.Clients,
            (w, _) =>
            {
                var n = w.Next();
                return Task.FromResult(new ProbeRequest(Routes.Clients, new CreateClientRequest(
                    "عميل " + n, "Probe client " + n, OrganizationTypes.SchoolGroup, "مدرسة", "School", "PC" + n,
                    $"probe-client-{n}-{Guid.NewGuid():N}@clients.test", "مدير", "Admin")));
            },
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Created)),
        new("clients update", HttpMethod.Put, Routes.ClientPattern,
            (w, _) => Task.FromResult(new ProbeRequest(
                Routes.Client(w.Callers.A.Org.Id), new UpdateClientRequest(w.Callers.A.Org.NameAr, w.Callers.A.Org.NameEn, "Active"))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok)),
    ];
}
