using Lms.Domain.Organizations;
using Lms.Shared;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Org units (Task 9, #99): unit-targeted, target unit below School1 (Review Focus 1: sibling/other client 404).</summary>
public sealed class OrgUnitsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("org-units list", HttpMethod.Get, Routes.OrgUnits,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.OrgUnits)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("org-units get", HttpMethod.Get, Routes.OrgUnitPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnit((await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("org-units create (under a unit below School1)", HttpMethod.Post, Routes.OrgUnits,
            async (w, _) =>
            {
                var parent = await w.NewUnitAsync(w.Callers.A.School1);
                return new ProbeRequest(Routes.OrgUnits, new CreateOrgUnitRequest(
                    parent.Id, await NewDeeperLevelIdAsync(w, parent), "وحدة", "Probe child", "C" + w.Next()));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("org-units update", HttpMethod.Put, Routes.OrgUnitPattern,
            async (w, _) =>
            {
                var unit = await NewDeeperUnitAsync(w);
                return new ProbeRequest(Routes.OrgUnit(unit.Id), new UpdateOrgUnitRequest(unit.LevelTypeId, "وحدة", "Renamed", unit.Code));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("org-units status", HttpMethod.Put, Routes.OrgUnitStatusPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitStatus((await w.NewUnitAsync(w.Callers.A.School1)).Id),
                new SetOrgUnitStatusRequest(OrgUnitStatuses.Inactive)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("org-units delete (leaf)", HttpMethod.Delete, Routes.OrgUnitPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnit((await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    /// <summary>A level type of client A that is deeper than the unit's own (created directly; orders need not be dense).</summary>
    private static async Task<Guid> NewDeeperLevelIdAsync(M1ProbeWorld w, OrgUnit parent)
    {
        await using var db = w.Callers.Factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.PlatformAdmin());
        var parentLevel = await db.LevelTypes.SingleAsync(l => l.Id == parent.LevelTypeId);
        var level = new LevelType(w.Callers.A.Org.Id, "مستوى", "Probe deeper level", parentLevel.Order + 1 + w.Next() % 1000, true);
        db.Add(level);
        await db.SaveChangesAsync();
        return level.Id;
    }

    /// <summary>A unit below School1 whose level is deeper than School1's, so updating it keeps the depth rule satisfied.</summary>
    private static async Task<OrgUnit> NewDeeperUnitAsync(M1ProbeWorld w)
    {
        var school1 = w.Callers.A.School1;
        var levelId = await NewDeeperLevelIdAsync(w, school1);
        await using var db = w.Callers.Factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.PlatformAdmin());
        var unit = OrgUnit.CreateChild(school1, levelId, "وحدة", "Probe deeper unit", "D" + w.Next());
        db.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }
}
