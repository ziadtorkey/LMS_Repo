using Lms.Shared;
using Lms.Shared.Grading;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Grading scales and weight templates of an org unit (M5 Task 4). Everything hangs off School1: School 2 and the other client get 404 (an
/// invisible unit), a Teacher may read (<c>Grades.View</c>) but not configure (403, <c>Grades.ConfigureScales</c>).
/// </summary>
public sealed class GradingScaleProbeRows : IProbeRowModule
{
    private static readonly SaveGradingScaleRequest ScaleBody = new(
        "سلم", "Scale", ScaleKinds.Letter,
        [new(0, 0, 60, "F", null, null, null, null, null), new(1, 60, 100, "P", null, null, null, null, null)]);

    private static readonly SaveWeightTemplateRequest TemplateBody = new(
        "قالب", "Template", [new(null, "اختبارات", "Exams", 60, 0), new(null, "واجبات", "Homework", 40, 1)]);

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M5 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("grading scales list", HttpMethod.Get, Routes.OrgUnitGradingScalesPattern,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.OrgUnitGradingScales(w.Callers.A.School1.Id))),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("grading scales create", HttpMethod.Post, Routes.OrgUnitGradingScalesPattern,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.OrgUnitGradingScales(w.Callers.A.School1.Id), ScaleBody)),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("grading scale get", HttpMethod.Get, Routes.GradingScalePattern,
            async (w, _) => new ProbeRequest(await ScaleUrlAsync(w)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("grading scale update", HttpMethod.Put, Routes.GradingScalePattern,
            async (w, _) => new ProbeRequest(await ScaleUrlAsync(w), ScaleBody),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("grading scale delete", HttpMethod.Delete, Routes.GradingScalePattern,
            async (w, _) => new ProbeRequest(await ScaleUrlAsync(w)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
        new("grading scale confirm", HttpMethod.Post, Routes.GradingScaleConfirmPattern,
            async (w, _) => new ProbeRequest(await ScaleUrlAsync(w, "/confirm")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("grading scale default", HttpMethod.Post, Routes.GradingScaleDefaultPattern,
            async (w, _) => new ProbeRequest(await ScaleUrlAsync(w, "/default")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("grading scale preset copy", HttpMethod.Post, Routes.OrgUnitGradingScalePresetsPattern,
            (w, _) => Task.FromResult(new ProbeRequest(
                Routes.OrgUnitGradingScalePresets(w.Callers.A.School1.Id), new CopyScalePresetRequest("sa-school-5band"))),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("weight templates list", HttpMethod.Get, Routes.OrgUnitWeightTemplatesPattern,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.OrgUnitWeightTemplates(w.Callers.A.School1.Id))),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("weight templates create", HttpMethod.Post, Routes.OrgUnitWeightTemplatesPattern,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.OrgUnitWeightTemplates(w.Callers.A.School1.Id), TemplateBody)),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("weight template get", HttpMethod.Get, Routes.WeightTemplatePattern,
            async (w, _) => new ProbeRequest(await TemplateUrlAsync(w)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("weight template update", HttpMethod.Put, Routes.WeightTemplatePattern,
            async (w, _) => new ProbeRequest(await TemplateUrlAsync(w), TemplateBody),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("weight template delete", HttpMethod.Delete, Routes.WeightTemplatePattern,
            async (w, _) => new ProbeRequest(await TemplateUrlAsync(w)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static async Task<string> ScaleUrlAsync(M1ProbeWorld w, string suffix = "")
    {
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.OrgUnitGradingScales(w.Callers.A.School1.Id), ScaleBody);
        return Routes.GradingScale(w.Callers.A.School1.Id, created.GetProperty("id").GetGuid()) + suffix;
    }

    private static async Task<string> TemplateUrlAsync(M1ProbeWorld w)
    {
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.OrgUnitWeightTemplates(w.Callers.A.School1.Id), TemplateBody);
        return Routes.WeightTemplate(w.Callers.A.School1.Id, created.GetProperty("id").GetGuid());
    }
}
