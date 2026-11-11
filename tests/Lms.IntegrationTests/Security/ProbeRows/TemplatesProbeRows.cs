using Lms.Domain.Organizations;
using Lms.Shared;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Branch templates (M1 Task 17, #52): saved from a unit the caller manages, applied under a unit they manage.</summary>
public sealed class TemplatesProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("templates list", HttpMethod.Get, Routes.Templates,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Templates)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("templates get (client A shared template)", HttpMethod.Get, Routes.TemplatePattern,
            async (w, _) => new ProbeRequest(Routes.Template((await NewTemplateAsync(w)).Id)),
            Expect(Ok, Ok, Ok, Forbidden, NotFound, Ok)),
        new("templates save (from School1)", HttpMethod.Post, Routes.Templates,
            async (w, _) => new ProbeRequest(Routes.Templates, await TemplateBodyAsync(w)),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("templates apply (under School1)", HttpMethod.Post, Routes.TemplateApplyPattern,
            async (w, _) =>
            {
                var (id, _) = await NewTemplateAsync(w);
                // the unit code is a placeholder so that every apply creates a different code
                return new ProbeRequest(Routes.TemplateApply(id), new ApplyTemplateRequest(
                    (await w.NewUnitAsync(w.Callers.A.School1)).Id, new Dictionary<string, string> { ["Ref"] = "A" + Guid.NewGuid().ToString("N")[..8] }));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("templates delete (client A template)", HttpMethod.Delete, Routes.TemplatePattern,
            async (w, _) => new ProbeRequest(Routes.Template((await NewTemplateAsync(w)).Id)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    /// <summary>A template of a unit below School1 whose level is deeper than the units it is later applied under.</summary>
    private static async Task<SaveTemplateRequest> TemplateBodyAsync(M1ProbeWorld w)
    {
        var n = w.Next();
        var source = await NewDeeperUnitAsync(w);
        return new SaveTemplateRequest(source.Id, "قالب " + n, "Probe template " + n, "Shared", false, false,
            new Dictionary<string, string> { [source.Code] = "{Ref}" });
    }

    private static async Task<(Guid Id, string Code)> NewTemplateAsync(M1ProbeWorld w)
    {
        var body = await TemplateBodyAsync(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Templates, body);
        return (created.GetProperty("summary").GetProperty("id").GetGuid(), body.Parameterize.Keys.Single());
    }

    private static async Task<OrgUnit> NewDeeperUnitAsync(M1ProbeWorld w)
    {
        var school1 = w.Callers.A.School1;
        await using var db = w.Callers.Factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.PlatformAdmin());
        var parentLevel = await db.LevelTypes.SingleAsync(l => l.Id == school1.LevelTypeId);
        var level = new LevelType(w.Callers.A.Org.Id, "مستوى", "Probe template level", parentLevel.Order + 1 + w.Next() % 1000, true);
        db.Add(level);
        var unit = OrgUnit.CreateChild(school1, level.Id, "وحدة", "Probe template source", "T" + w.Next());
        db.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }
}
