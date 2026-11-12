using Lms.Domain.Blueprints;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Table-level isolation for the blueprint and class lesson plan tables (M3 plan Task 16, PRD §4.2): client B never sees
/// client A's rows, a sibling school never sees its sibling's, a parent sees its descendants', soft-deleted rows are hidden
/// from everyone, and a caller with no client or a malformed scope sees nothing. The API reads an inherited course's
/// blueprint through <c>BlueprintReader</c> (explicit client + course filters); these tests cover the global filters that
/// every other read path relies on.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M3BlueprintTableIsolationTests(LmsApiFactory factory)
{
    /// <summary>One row of every blueprint/plan table at each of the four places: School1, School2, client B root, client A root.</summary>
    private sealed record Place(Blueprint Blueprint, BlueprintSection BSection, BlueprintLesson BLesson,
        ClassLessonPlan Plan, ClassSection CSection, ClassLesson CLesson);

    private static TestCurrentContext In(AccessTree tree, OrgUnit scope) => TestCurrentContext.For(tree.Org.Id, scope.Path);

    private async Task<Place> AddPlaceAsync(OrgUnit unit)
    {
        var rows = await AcademicTestKit.AddAllAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var blueprint = Blueprint.CreateDraft(rows.Course);
        var bSection = new BlueprintSection(blueprint, Guid.NewGuid(), 0, "قسم", "Section");
        var bLesson = new BlueprintLesson(bSection, Guid.NewGuid(), 0, "درس", "Lesson", null, null, null);
        var plan = new ClassLessonPlan(rows.ClassCourse, null, null, null);
        var cSection = new ClassSection(plan, Guid.NewGuid(), 0, "قسم", "Section");
        var cLesson = new ClassLesson(cSection, Guid.NewGuid(), 0, "درس", "Lesson", null, null, null);
        db.AddRange(blueprint, bSection, bLesson, plan, cSection, cLesson);
        await db.SaveChangesAsync();
        return new Place(blueprint, bSection, bLesson, plan, cSection, cLesson);
    }

    private async Task SoftDeleteAsync<T>(Guid id) where T : class
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Remove(await db.Set<T>().SingleAsync(e => EF.Property<Guid>(e, "Id") == id));
        await db.SaveChangesAsync();
    }

    private async Task AssertIsolatedAsync<T>(
        Func<Place, Guid> idOf, Place school1, Place school2, Place clientB, Place rootA, AccessTree a, AccessTree b, bool softDeletes)
        where T : class
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
            await SoftDeleteAsync<T>(idOf(school1));
            Assert.Equal(new[] { idOf(school2), idOf(rootA) }.Order(), await VisibleTo(In(a, a.Root)));
            Assert.Empty(await VisibleTo(In(a, a.School1)));
        }
    }

    [Fact]
    public async Task Blueprint_tables_and_class_lesson_plan_tables_are_isolated_by_client_subtree_and_soft_delete()
    {
        var a = await AccessTestData.SeedAsync(factory, "M3BpA");
        var b = await AccessTestData.SeedAsync(factory, "M3BpB");
        var school1 = await AddPlaceAsync(a.School1);
        var school2 = await AddPlaceAsync(a.School2);
        var clientB = await AddPlaceAsync(b.Root);
        var rootA = await AddPlaceAsync(a.Root);

        // Child rows first (their parents are deleted last, as the app would never delete a parent first).
        await AssertIsolatedAsync<BlueprintLesson>(p => p.BLesson.Id, school1, school2, clientB, rootA, a, b, softDeletes: false);
        await AssertIsolatedAsync<BlueprintSection>(p => p.BSection.Id, school1, school2, clientB, rootA, a, b, softDeletes: false);
        await AssertIsolatedAsync<ClassLesson>(p => p.CLesson.Id, school1, school2, clientB, rootA, a, b, softDeletes: false);
        await AssertIsolatedAsync<ClassSection>(p => p.CSection.Id, school1, school2, clientB, rootA, a, b, softDeletes: false);
        await AssertIsolatedAsync<ClassLessonPlan>(p => p.Plan.Id, school1, school2, clientB, rootA, a, b, softDeletes: true);
        await AssertIsolatedAsync<Blueprint>(p => p.Blueprint.Id, school1, school2, clientB, rootA, a, b, softDeletes: true);
    }
}
