using Lms.Shared;
using Lms.Shared.Organizations;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Level types (Task 8, #83): client-wide. Reads at any scope; writes only at the client root (Review Focus 4).</summary>
public sealed class LevelTypesProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("level-types list", HttpMethod.Get, Routes.LevelTypes,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LevelTypes)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Forbidden)),
        new("level-types create", HttpMethod.Post, Routes.LevelTypes,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.LevelTypes, LevelTypeBody(w))),
            Expect(Created, Forbidden, Forbidden, Forbidden, Created, Forbidden)),
        new("level-types update (client A type)", HttpMethod.Put, Routes.LevelTypePattern,
            async (w, _) => new ProbeRequest(Routes.LevelType(await NewLevelTypeAsync(w)), LevelTypeBody(w)),
            Expect(Ok, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
        new("level-types reorder (client A ids)", HttpMethod.Put, Routes.LevelTypesOrder,
            async (w, _) => new ProbeRequest(Routes.LevelTypesOrder, new ReorderLevelTypesRequest(await LevelTypeIdsOfAAsync(w))),
            Expect(NoContent, Forbidden, Forbidden, Forbidden, BadRequest, Forbidden)),
        new("level-types delete (client A type)", HttpMethod.Delete, Routes.LevelTypePattern,
            async (w, _) => new ProbeRequest(Routes.LevelType(await NewLevelTypeAsync(w))),
            Expect(NoContent, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
    ];

    private static SaveLevelTypeRequest LevelTypeBody(M1ProbeWorld w)
    {
        var n = w.Next();
        return new SaveLevelTypeRequest("مستوى " + n, "Probe level " + n, false);
    }

    private static async Task<Guid> NewLevelTypeAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.LevelTypes, LevelTypeBody(w))).GetProperty("id").GetGuid();

    private static async Task<IReadOnlyList<Guid>> LevelTypeIdsOfAAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Get, Routes.LevelTypes)).EnumerateArray()
        .OrderBy(t => t.GetProperty("order").GetInt32())
        .Select(t => t.GetProperty("id").GetGuid())
        .ToList();
}
