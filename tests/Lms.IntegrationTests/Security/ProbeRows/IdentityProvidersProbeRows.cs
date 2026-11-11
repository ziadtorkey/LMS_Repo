using Lms.Shared;
using Lms.Shared.IdentityProviders;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Identity providers (M2 Task 21, #167): client-wide like level types; every call needs the permission at the client root.</summary>
public sealed class IdentityProvidersProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("identity-providers list", HttpMethod.Get, Routes.IdentityProviders,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.IdentityProviders)),
            Expect(Ok, Forbidden, Forbidden, Forbidden, Ok, Forbidden)),
        new("identity-providers create", HttpMethod.Post, Routes.IdentityProviders,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.IdentityProviders, ProviderBody(w))),
            Expect(Created, Forbidden, Forbidden, Forbidden, Created, Forbidden)),
        new("identity-providers update (client A provider)", HttpMethod.Put, Routes.IdentityProviderPattern,
            async (w, _) => { var (id, body) = await NewProviderAsync(w); return new ProbeRequest(Routes.IdentityProvider(id), body); },
            Expect(Ok, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
        new("identity-providers delete (client A provider)", HttpMethod.Delete, Routes.IdentityProviderPattern,
            async (w, _) => new ProbeRequest(Routes.IdentityProvider((await NewProviderAsync(w)).Id)),
            Expect(NoContent, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
        new("identity-providers test (client A provider)", HttpMethod.Post, Routes.IdentityProviderTestPattern,
            async (w, _) => new ProbeRequest(Routes.IdentityProviderTest((await NewProviderAsync(w)).Id)),
            Expect(Ok, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
    ];

    private static SaveIdentityProviderRequest ProviderBody(M1ProbeWorld w) => new(
        IdentityProviderKinds.AzureAd, "probe-" + w.Next() + "-" + Guid.NewGuid().ToString("N")[..8],
        "مزود", "Probe provider", null, "probe-tenant", "client-id", "probe-secret", "openid profile email",
        ExternalMatchBy.Email, null, ["probe.example"], true);

    private static async Task<(Guid Id, SaveIdentityProviderRequest Body)> NewProviderAsync(M1ProbeWorld w)
    {
        var body = ProviderBody(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.IdentityProviders, body);
        return (created.GetProperty("id").GetGuid(), body with { ClientSecret = null });
    }
}
