using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Grading;

/// <summary>
/// Table-level isolation for GradingScale, GradingScaleBand, WeightTemplate and WeightTemplateItem (M5 Task 4, PRD §4.2): client B never sees client
/// A's rows, a sibling school never sees its sibling's, a parent sees its descendants', soft-deleted rows are hidden, and a caller with no client
/// or a malformed scope sees nothing. The API reads ancestors through <c>ScaleResolver</c> (explicit client + path filters, bypass site 1).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GradingScaleIsolationTests(LmsApiFactory factory)
{
    private sealed record Place(GradingScale Scale, GradingScaleBand Band, WeightTemplate Template, WeightTemplateItem Item);

    private static TestCurrentContext In(AccessTree tree, OrgUnit scope) => TestCurrentContext.For(tree.Org.Id, scope.Path);

    private async Task<Place> AddPlaceAsync(OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var ownerUnit = await db.Set<OrgUnit>().SingleAsync(u => u.Id == unit.Id);
        var scale = GradingScale.CreatePlain(ownerUnit, "سلم", "Scale", "Letter", isDefault: false);
        var band = new GradingScaleBand(scale, 0, 0, 100, "ALL", null, null, null, null, null);
        var template = new WeightTemplate(ownerUnit, "قالب", "Template");
        var item = new WeightTemplateItem(template, 0, "فئة", "Category", 100);
        db.AddRange(scale, band, template, item);
        await db.SaveChangesAsync();
        return new Place(scale, band, template, item);
    }

    private async Task AssertIsolatedAsync<T>(
        Func<Place, Guid> idOf, Place school1, Place school2, Place clientB, Place rootA, AccessTree a, AccessTree b, bool softDeletes) where T : class
    {
        var all = new[] { idOf(school1), idOf(school2), idOf(clientB), idOf(rootA) };

        async Task<List<Guid>> VisibleTo(TestCurrentContext who)
        {
            await using var db = factory.CreateDbContext(who);
            var visible = await db.Set<T>().Select(e => EF.Property<Guid>(e, "Id")).ToListAsync();
            return visible.Where(all.Contains).Order().ToList();
        }

        Assert.Equal([idOf(clientB)], await VisibleTo(In(b, b.Root)));
        Assert.Equal([idOf(school1)], await VisibleTo(In(a, a.School1)));
        Assert.Equal([idOf(school2)], await VisibleTo(In(a, a.School2)));
        Assert.Equal(new[] { idOf(school1), idOf(school2), idOf(rootA) }.Order(), await VisibleTo(In(a, a.Root)));
        Assert.Empty(await VisibleTo(TestCurrentContext.For(a.Org.Id, null)));
        Assert.Empty(await VisibleTo(new TestCurrentContext { UserId = Guid.NewGuid() }));
        Assert.Empty(await VisibleTo(TestCurrentContext.For(a.Org.Id, a.School1.Path[..^1])));
        Assert.Equal(all.Order(), await VisibleTo(TestCurrentContext.PlatformAdmin()));

        if (softDeletes)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            db.Remove(await db.Set<T>().SingleAsync(e => EF.Property<Guid>(e, "Id") == idOf(school1)));
            await db.SaveChangesAsync();
            Assert.Equal(new[] { idOf(school2), idOf(rootA) }.Order(), await VisibleTo(In(a, a.Root)));
            Assert.Empty(await VisibleTo(In(a, a.School1)));
        }
    }

    [Fact]
    public async Task Scale_and_weight_template_tables_are_isolated_by_client_subtree_and_soft_delete()
    {
        var a = await AccessTestData.SeedAsync(factory, "GSIsoA");
        var b = await AccessTestData.SeedAsync(factory, "GSIsoB");
        var school1 = await AddPlaceAsync(a.School1);
        var school2 = await AddPlaceAsync(a.School2);
        var clientB = await AddPlaceAsync(b.Root);
        var rootA = await AddPlaceAsync(a.Root);

        // Bands and items have no soft delete (they are replaced wholesale), so they are checked without it.
        await AssertIsolatedAsync<GradingScaleBand>(p => p.Band.Id, school1, school2, clientB, rootA, a, b, softDeletes: false);
        await AssertIsolatedAsync<WeightTemplateItem>(p => p.Item.Id, school1, school2, clientB, rootA, a, b, softDeletes: false);
        await AssertIsolatedAsync<GradingScale>(p => p.Scale.Id, school1, school2, clientB, rootA, a, b, softDeletes: true);
        await AssertIsolatedAsync<WeightTemplate>(p => p.Template.Id, school1, school2, clientB, rootA, a, b, softDeletes: true);
    }
}
