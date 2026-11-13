using Lms.Domain.Content;
using Lms.Domain.Files;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Content;

/// <summary>Every content table is isolated by client and by org-unit subtree (rule 1). One test per table.</summary>
[Collection(ApiCollection.Name)]
public sealed class ContentIsolationTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Asset_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "CI1A");
        var b = await AccessTestData.SeedAsync(factory, "CI1B");
        var inSchool1 = await AddAssetAsync(a.Root, a.School1);
        var inSchool2 = await AddAssetAsync(a.Root, a.School2);
        var otherClient = await AddAssetAsync(b.Root, b.School1);

        Assert.Equal(
            Sorted(inSchool1, inSchool2),
            await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), inSchool1, inSchool2, otherClient));
    }

    [Fact]
    public async Task Library_asset_without_class_course_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "CI2A");
        var b = await AccessTestData.SeedAsync(factory, "CI2B");
        var inSchool1 = await AddLibraryAssetAsync(a.School1);
        var inSchool2 = await AddLibraryAssetAsync(a.School2);
        var otherClient = await AddLibraryAssetAsync(b.School1);

        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal(Sorted(inSchool1, inSchool2), await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), inSchool1, inSchool2, otherClient));
    }

    private async Task<Guid> AddLibraryAssetAsync(Lms.Domain.Organizations.OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var file = new StoredFile(unit.OrganizationId, unit.Id, unit.Path, "c.png", "image/png", 1, DateTime.UtcNow);
        var asset = new Asset(unit, file.Id, AssetKinds.Image, "c.png", "image/png", 1);
        db.AddRange(file, asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<Guid> AddAssetAsync(Lms.Domain.Organizations.OrgUnit root, Lms.Domain.Organizations.OrgUnit unit)
    {
        var year = await AcademicTestKit.AddYearAsync(factory, root);
        var cls = await AcademicTestKit.AddClassAsync(factory, unit, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, unit);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var file = new StoredFile(
            classCourse.OrganizationId, classCourse.OrgUnitId, classCourse.OrgUnitPath, "a.pdf", "application/pdf", 1, DateTime.UtcNow);
        var asset = new Asset(classCourse, file.Id, AssetKinds.Document, "a.pdf", "application/pdf", 1);
        db.AddRange(file, asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private async Task<List<Guid>> VisibleAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<Asset>().Select(x => x.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();
}
