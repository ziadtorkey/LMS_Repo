using Lms.Domain.Files;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Packages;

/// <summary>The package table is isolated by client and by org-unit subtree (rule 1, M4b Task 8a).</summary>
[Collection(ApiCollection.Name)]
public sealed class PackageIsolationTests(LmsApiFactory factory)
{
    [Fact]
    public async Task PackageUpload_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "PIA");
        var b = await AccessTestData.SeedAsync(factory, "PIB");
        var inSchool1 = await AddUploadAsync(a.Root, a.School1);
        var inSchool2 = await AddUploadAsync(a.Root, a.School2);
        var otherClient = await AddUploadAsync(b.Root, b.School1);

        Assert.Equal(Sorted(inSchool1, inSchool2), await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), inSchool1, inSchool2, otherClient));
    }

    [Fact]
    public async Task A_deleted_package_upload_is_invisible()
    {
        var a = await AccessTestData.SeedAsync(factory, "PID");
        var id = await AddUploadAsync(a.Root, a.School1);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<PackageUpload>().SingleAsync(p => p.Id == id));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), id));
    }

    private async Task<Guid> AddUploadAsync(Lms.Domain.Organizations.OrgUnit root, Lms.Domain.Organizations.OrgUnit unit)
    {
        var year = await AcademicTestKit.AddYearAsync(factory, root);
        var cls = await AcademicTestKit.AddClassAsync(factory, unit, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, unit);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var file = new StoredFile(classCourse.OrganizationId, classCourse.OrgUnitId, classCourse.OrgUnitPath, "package.zip", "application/zip", 8, DateTime.UtcNow);
        var upload = new PackageUpload(classCourse, Guid.NewGuid(), PackageKinds.Scorm, "course.zip", file.Id, 8, new string('b', 64));
        db.AddRange(file, upload);
        await db.SaveChangesAsync();
        return upload.Id;
    }

    private async Task<List<Guid>> VisibleAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<PackageUpload>().Select(x => x.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();
}
