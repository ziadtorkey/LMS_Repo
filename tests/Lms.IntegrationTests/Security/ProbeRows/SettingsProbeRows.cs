using Lms.Shared;
using Lms.Shared.Configuration;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Settings (Task 14, #100): the catalog is client-agnostic; unit settings are checked at the target unit.</summary>
public sealed class SettingsProbeRows : IProbeRowModule
{
    private const string ProbeSetting = "Ui.ShowHijriDates";

    public IEnumerable<ProbeRow> Rows =>
    [
        new("settings catalog", HttpMethod.Get, Routes.SettingsCatalog,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.SettingsCatalog)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("unit settings read", HttpMethod.Get, Routes.OrgUnitSettingsPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitSettings((await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("unit setting set", HttpMethod.Put, Routes.OrgUnitSettingPattern,
            async (w, _) => new ProbeRequest(
                Routes.OrgUnitSetting((await w.NewUnitAsync(w.Callers.A.School1)).Id, ProbeSetting), new SetSettingRequest("true", false)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("unit setting remove", HttpMethod.Delete, Routes.OrgUnitSettingPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitSetting(await NewUnitWithSettingAsync(w), ProbeSetting)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static async Task<Guid> NewUnitWithSettingAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Put, Routes.OrgUnitSetting(unit.Id, ProbeSetting), new SetSettingRequest("true", false));
        return unit.Id;
    }
}
