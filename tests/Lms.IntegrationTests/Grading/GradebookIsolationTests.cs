using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Grading.GradebookTestKit;

namespace Lms.IntegrationTests.Grading;

/// <summary>Rule 1: every new table is invisible across clients and sibling units (M5 Task 7).</summary>
[Collection(ApiCollection.Name)]
public sealed class GradebookIsolationTests(LmsApiFactory factory)
{
    private sealed record Rows(Guid Gradebook, Guid Category, Guid Item, Guid Entry);

    private async Task<Rows> AddAsync(OrgUnit unit)
    {
        var world = await NewWorldAsync(factory, unit);
        var student = await PeopleTestKit.AddStudentAsync(factory, unit, PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var gradebook = new Gradebook(world.ClassCourse, world.First.Id, null, 2, "PointsWeighted", "Normalise");
        var category = new GradeCategory(gradebook, "فئة", "Category", 100m, 1);
        var item = GradeItem.Manual(gradebook, category, "بند", "Item", 10m, 1);
        var entry = new GradeEntry(item, student.Id, null, DateTime.UtcNow);
        db.AddRange(gradebook, category, item, entry);
        await db.SaveChangesAsync();
        return new Rows(gradebook.Id, category.Id, item.Id, entry.Id);
    }

    private async Task<List<Guid>> VisibleAsync<T>(TestCurrentContext who, params Guid[] among) where T : Lms.Domain.Common.Entity
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<T>().Select(x => x.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private async Task AssertIsolatedAsync<T>(Func<Rows, Guid> pick) where T : Lms.Domain.Common.Entity
    {
        var a = await AccessTestData.SeedAsync(factory, "GB" + typeof(T).Name[..5] + "A");
        var b = await AccessTestData.SeedAsync(factory, "GB" + typeof(T).Name[..5] + "B");
        var inSchool1 = pick(await AddAsync(a.School1));
        var inSchool2 = pick(await AddAsync(a.School2));
        var otherClient = pick(await AddAsync(b.School1));
        var all = new[] { inSchool1, inSchool2, otherClient };

        Assert.Equal(new[] { inSchool1, inSchool2 }.Order(), await VisibleAsync<T>(TestCurrentContext.For(a.Org.Id, a.Root.Path), all));
        Assert.Equal([inSchool1], await VisibleAsync<T>(TestCurrentContext.For(a.Org.Id, a.School1.Path), all));
        Assert.Equal([inSchool2], await VisibleAsync<T>(TestCurrentContext.For(a.Org.Id, a.School2.Path), all));
        Assert.Equal([otherClient], await VisibleAsync<T>(TestCurrentContext.For(b.Org.Id, b.Root.Path), all));
    }

    [Fact]
    public Task Gradebook_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<Gradebook>(r => r.Gradebook);

    [Fact]
    public Task GradeCategory_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<GradeCategory>(r => r.Category);

    [Fact]
    public Task GradeItem_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<GradeItem>(r => r.Item);

    [Fact]
    public Task GradeEntry_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<GradeEntry>(r => r.Entry);

    [Fact]
    public async Task A_deleted_gradebook_is_invisible_and_frees_its_class_course_and_term()
    {
        var a = await AccessTestData.SeedAsync(factory, "GBDEL");
        var world = await NewWorldAsync(factory, a.School1);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new Gradebook(world.ClassCourse, world.First.Id, null, 2, "PointsWeighted", "Normalise"));
            await db.SaveChangesAsync();
            db.Remove(await db.Set<Gradebook>().SingleAsync(g => g.ClassCourseId == world.ClassCourse.Id));
            await db.SaveChangesAsync();
        }

        await using var again = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        again.Add(new Gradebook(world.ClassCourse, world.First.Id, null, 2, "PointsWeighted", "Normalise")); // the unique key is free again
        await again.SaveChangesAsync();
        Assert.Single(await again.Set<Gradebook>().Where(g => g.ClassCourseId == world.ClassCourse.Id).ToListAsync());
    }

    [Fact]
    public async Task A_second_live_gradebook_for_the_same_class_course_and_term_is_refused()
    {
        var a = await AccessTestData.SeedAsync(factory, "GBDUP");
        var world = await NewWorldAsync(factory, a.School1);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new Gradebook(world.ClassCourse, world.First.Id, null, 2, "PointsWeighted", "Normalise"));
        await db.SaveChangesAsync();
        db.Add(new Gradebook(world.ClassCourse, world.First.Id, null, 2, "PointsWeighted", "Normalise"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
