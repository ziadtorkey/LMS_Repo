using Lms.Shared;
using Lms.Shared.Timetable;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Periods of a unit's school day (M5 Task 13): read with Timetable.View, written with Timetable.Configure, checked at the unit.</summary>
public sealed class PeriodProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("periods list", HttpMethod.Get, Routes.OrgUnitPeriodsPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitPeriods((await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("periods create", HttpMethod.Post, Routes.OrgUnitPeriodsPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitPeriods((await w.NewUnitAsync(w.Callers.A.School1)).Id), Body()),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("periods update", HttpMethod.Put, Routes.PeriodPattern,
            async (w, _) =>
            {
                var (unitId, periodId) = await NewPeriodAsync(w);
                return new ProbeRequest(Routes.Period(unitId, periodId), Body());
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("periods delete", HttpMethod.Delete, Routes.PeriodPattern,
            async (w, _) =>
            {
                var (unitId, periodId) = await NewPeriodAsync(w);
                return new ProbeRequest(Routes.Period(unitId, periodId));
            },
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static SavePeriodRequest Body() => new("حصة", "Probe period", new TimeOnly(8, 0), new TimeOnly(8, 45), 1);

    private static async Task<(Guid UnitId, Guid PeriodId)> NewPeriodAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.OrgUnitPeriods(unit.Id), Body());
        return (unit.Id, created.GetProperty("id").GetGuid());
    }
}
