using System.Net.Http.Headers;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Files;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Assets of a class course (M4a Task 4): everything is checked at the class unit (School1). School 2 and the other client
/// get 404. The Teacher holds Modules.* only for class courses they teach, and the probe's class course has no teacher
/// assignment, so every row answers 404 for the Teacher (the relationship rule).
/// </summary>
public sealed class AssetsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4a });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("assets upload", HttpMethod.Post, Routes.Assets,
            async (w, _) => new ProbeRequest(Routes.Assets, Body((await NewClassCourseAsync(w)).Id)),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("assets limits", HttpMethod.Get, Routes.AssetsLimits,
            async (w, _) => new ProbeRequest(Routes.AssetsLimitsFor((await NewClassCourseAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("assets get", HttpMethod.Get, Routes.AssetPattern,
            async (w, _) => new ProbeRequest(Routes.Asset((await NewAssetAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("assets url", HttpMethod.Get, Routes.AssetUrlPattern,
            async (w, _) => new ProbeRequest(Routes.AssetUrl((await NewAssetAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
    ];

    private static MultipartFormDataContent Body(Guid classCourseId)
    {
        var file = new ByteArrayContent("%PDF-1.7 probe"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent
        {
            { file, AssetUploadForm.File, "probe.pdf" },
            { new StringContent(classCourseId.ToString()), AssetUploadForm.ClassCourseId },
        };
    }

    /// <summary>A class at School1 (year defined at School1, see the order-dependence note in ClassesProbeRows) with one course.</summary>
    private static async Task<ClassCourse> NewClassCourseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        return await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
    }

    private static async Task<Asset> NewAssetAsync(M1ProbeWorld w)
    {
        var classCourse = await NewClassCourseAsync(w);
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var file = new StoredFile(
            classCourse.OrganizationId, classCourse.OrgUnitId, classCourse.OrgUnitPath, "probe.pdf", "application/pdf", 14, DateTime.UtcNow);
        var asset = new Asset(classCourse, file.Id, AssetKinds.Document, "probe.pdf", "application/pdf", 14);
        db.AddRange(file, asset);
        await db.SaveChangesAsync();
        return asset;
    }
}
