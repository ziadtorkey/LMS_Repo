using Lms.Domain.Packages;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>The revocation table is isolated by client and by org-unit subtree (rule 1, M4b Task 9a).</summary>
[Collection(ApiCollection.Name)]
public sealed class PackageRevocationIsolationTests(LmsApiFactory factory) : IDisposable
{
    private readonly PackageHostKit _kit = new(factory);

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task PackageRevocation_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "PRA");
        var b = await AccessTestData.SeedAsync(factory, "PRB");
        var inSchool1 = await AddRevocationAsync(await _kit.SeedPackageAsync(a.Root, a.School1));
        var inSchool2 = await AddRevocationAsync(await _kit.SeedPackageAsync(a.Root, a.School2));
        var otherClient = await AddRevocationAsync(await _kit.SeedPackageAsync(b.Root, b.School1));

        Assert.Equal(Sorted(inSchool1, inSchool2), await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), inSchool1, inSchool2, otherClient));
    }

    private async Task<Guid> AddRevocationAsync(PackageUpload package)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<PackageUpload>().SingleAsync(p => p.Id == package.Id);
        var revocation = new PackageRevocation(tracked, null, DateTime.UtcNow);
        db.Add(revocation);
        await db.SaveChangesAsync();
        return revocation.Id;
    }

    private async Task<List<Guid>> VisibleAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<PackageRevocation>().Select(x => x.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();
}
