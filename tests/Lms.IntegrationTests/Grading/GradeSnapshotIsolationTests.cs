using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Grading.GradebookTestKit;

namespace Lms.IntegrationTests.Grading;

/// <summary>Rule 1: the GradeSnapshot table is invisible across clients and sibling units (M5 Task 11).</summary>
[Collection(ApiCollection.Name)]
public sealed class GradeSnapshotIsolationTests(LmsApiFactory factory)
{
    private async Task<Guid> AddAsync(OrgUnit unit)
    {
        var world = await NewWorldAsync(factory, unit);
        var student = await PeopleTestKit.AddStudentAsync(factory, unit, PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var gradebook = new Gradebook(world.ClassCourse, world.First.Id, null, 2, "PointsWeighted", "Normalise");
        var snapshot = new GradeSnapshot(gradebook, student.Id, GradeSnapshot.PublishKind, Guid.NewGuid(), 1, "{}", null, DateTime.UtcNow);
        db.AddRange(gradebook, snapshot);
        await db.SaveChangesAsync();
        return snapshot.Id;
    }

    private async Task<List<Guid>> VisibleAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        return (await db.Set<GradeSnapshot>().Select(x => x.Id).ToListAsync()).Where(among.Contains).Order().ToList();
    }

    [Fact]
    public async Task GradeSnapshot_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "GSnA");
        var b = await AccessTestData.SeedAsync(factory, "GSnB");
        var inSchool1 = await AddAsync(a.School1);
        var inSchool2 = await AddAsync(a.School2);
        var otherClient = await AddAsync(b.School1);
        var all = new[] { inSchool1, inSchool2, otherClient };

        Assert.Equal(new[] { inSchool1, inSchool2 }.Order(), await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), all));
        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), all));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), all));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), all));
        Assert.Empty(await VisibleAsync(TestCurrentContext.For(a.Org.Id, null), all));
    }
}
