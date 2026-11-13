using Lms.Domain.Files;
using Lms.Domain.Organizations;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Packages;

/// <summary>The attempt table is isolated by client and by org-unit subtree (rule 1, M4b Task 9b): the same table-level checks as every unit-owned table.</summary>
[Collection(ApiCollection.Name)]
public sealed class PackageAttemptIsolationTests(LmsApiFactory factory)
{
    private static TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

    private async Task<Guid> AddAttemptAsync(OrgUnit unit)
    {
        var academic = await AcademicTestKit.AddAllAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = academic.ClassCourse;
        var file = new StoredFile(cc.OrganizationId, cc.OrgUnitId, cc.OrgUnitPath, "p.zip", "application/zip", 8, DateTime.UtcNow);
        var upload = new PackageUpload(cc, Guid.NewGuid(), PackageKinds.Scorm, "p.zip", file.Id, 8, new string('c', 64));
        var attempt = new PackageAttempt(upload, Guid.NewGuid(), academic.Student.Id, PackageCommitFormats.Scorm, DateTime.UtcNow);
        db.AddRange(file, upload);
        await db.SaveChangesAsync();
        db.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task<List<Guid>> VisibleAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<PackageAttempt>().Select(e => e.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    [Fact]
    public async Task PackageAttempt_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "PAIA");
        var b = await AccessTestData.SeedAsync(factory, "PAIB");
        var s1 = await AddAttemptAsync(a.School1);
        var s2 = await AddAttemptAsync(a.School2);
        var other = await AddAttemptAsync(b.Root);
        var atRoot = await AddAttemptAsync(a.Root);
        Guid[] all = [s1, s2, other, atRoot];

        Assert.Equal([other], await VisibleAsync(In(b, b.Root), all));
        Assert.Equal([s1], await VisibleAsync(In(a, a.School1), all));
        Assert.Equal([s2], await VisibleAsync(In(a, a.School2), all));
        Assert.Equal(new[] { s1, s2, atRoot }.Order().ToList(), await VisibleAsync(In(a, a.Root), all));
        Assert.Empty(await VisibleAsync(TestCurrentContext.For(a.Org.Id, null), all));
        Assert.Empty(await VisibleAsync(new TestCurrentContext { UserId = Guid.NewGuid() }, all));
        Assert.Equal(all.Length, (await VisibleAsync(TestCurrentContext.PlatformAdmin(), all)).Count);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<PackageAttempt>().SingleAsync(x => x.Id == s1));
            await db.SaveChangesAsync();
        }

        Assert.DoesNotContain(s1, await VisibleAsync(In(a, a.Root), all));
    }
}
