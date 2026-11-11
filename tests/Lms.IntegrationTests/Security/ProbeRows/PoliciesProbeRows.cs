using Lms.Shared;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Policies (Task 12, #94): unit-targeted like the settings rows; DELETE returns the remaining effective state (200).</summary>
public sealed class PoliciesProbeRows : IProbeRowModule
{
    private const string ProbePolicy = Lms.Shared.Authorization.PolicyCatalog.CreateOrgUnits;

    public IEnumerable<ProbeRow> Rows =>
    [
        new("policies catalog", HttpMethod.Get, Routes.PoliciesCatalog,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.PoliciesCatalog)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("unit policies read", HttpMethod.Get, Routes.OrgUnitPoliciesPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitPolicies((await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("unit policy enable", HttpMethod.Put, Routes.OrgUnitPolicyPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitPolicy((await w.NewUnitAsync(w.Callers.A.School1)).Id, ProbePolicy)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("unit policy disable", HttpMethod.Delete, Routes.OrgUnitPolicyPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitPolicy(await NewUnitWithPolicyAsync(w), ProbePolicy)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
    ];

    private static async Task<Guid> NewUnitWithPolicyAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Put, Routes.OrgUnitPolicy(unit.Id, ProbePolicy));
        return unit.Id;
    }
}
