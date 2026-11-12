using System.Reflection;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Content;

/// <summary>
/// Table-level isolation of the content tables (M4a Task 3 and 6, PRD 4.2): another client sees nothing, a sibling unit sees
/// nothing, a parent sees its descendants, no scope sees nothing, and a soft-deleted row disappears for everyone.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ModuleIsolationTests(LmsApiFactory factory)
{
    private sealed record Rows(Guid Module, Guid Content, Guid Completion);

    private sealed record Table(string Name, Type Type, Func<Rows, Guid> Id, bool SoftDeletable);

    private static readonly Table[] Tables =
    [
        new("LessonModule", typeof(LessonModule), r => r.Module, true),
        new("ModuleContent", typeof(ModuleContent), r => r.Content, true),
        new("ModuleCompletion", typeof(ModuleCompletion), r => r.Completion, false),
    ];

    private static TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

    private async Task<Rows> AddAsync(OrgUnit unit)
    {
        var academic = await AcademicTestKit.AddAllAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == academic.ClassCourse.Id);
        var plan = new ClassLessonPlan(classCourse, null, null, null);
        db.Add(plan);
        var section = new ClassSection(plan, Guid.NewGuid(), 1, "قسم", "Section");
        db.Add(section);
        var lesson = new ClassLesson(section, Guid.NewGuid(), 1, "درس", "Lesson", null, null, null);
        db.Add(lesson);
        var module = new LessonModule(classCourse, lesson, ModuleKinds.Page, 1);
        module.Apply("ص", "Page", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.View, null);
        db.Add(module);
        var content = new ModuleContent(module);
        content.Set(null, null, "<p>x</p>", null);
        db.Add(content);
        var completion = new ModuleCompletion(module, academic.Student.Id, CompletionStates.Completed, DateTime.UtcNow);
        db.Add(completion);
        await db.SaveChangesAsync();
        return new Rows(module.Id, content.Id, completion.Id);
    }

    private async Task<List<Guid>> VisibleAsync(Table table, TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var method = typeof(ModuleIsolationTests).GetMethod(nameof(IdsOf), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(table.Type);
        var ids = await (Task<List<Guid>>)method.Invoke(null, [db])!;
        return ids.Where(among.Contains).Order().ToList();
    }

    private static Task<List<Guid>> IdsOf<T>(LmsDbContext db) where T : class =>
        db.Set<T>().Select(e => EF.Property<Guid>(e, "Id")).ToListAsync();

    [Fact]
    public async Task Every_content_table_is_isolated_by_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "CI1A");
        var b = await AccessTestData.SeedAsync(factory, "CI1B");
        var s1 = await AddAsync(a.School1);
        var s2 = await AddAsync(a.School2);
        var other = await AddAsync(b.Root);
        var atRoot = await AddAsync(a.Root);

        foreach (var table in Tables)
        {
            Guid[] all = [table.Id(s1), table.Id(s2), table.Id(other), table.Id(atRoot)];

            Assert.True(new[] { table.Id(other) }.SequenceEqual(await VisibleAsync(table, In(b, b.Root), all)), table.Name + ": other client");
            Assert.True(new[] { table.Id(s1) }.SequenceEqual(await VisibleAsync(table, In(a, a.School1), all)), table.Name + ": school 1");
            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, In(a, a.School2), all)), table.Name + ": school 2");
            Assert.True(
                new[] { table.Id(s1), table.Id(s2), table.Id(atRoot) }.Order().SequenceEqual(await VisibleAsync(table, In(a, a.Root), all)),
                table.Name + ": parent");
            Assert.Empty(await VisibleAsync(table, TestCurrentContext.For(a.Org.Id, null), all));
            Assert.Empty(await VisibleAsync(table, new TestCurrentContext { UserId = Guid.NewGuid() }, all));
            Assert.True(all.Order().SequenceEqual(await VisibleAsync(table, TestCurrentContext.PlatformAdmin(), all)), table.Name + ": platform admin");
        }
    }

    [Fact]
    public async Task Soft_deleted_content_rows_disappear_for_everyone()
    {
        var a = await AccessTestData.SeedAsync(factory, "CI2");
        var s1 = await AddAsync(a.School1);
        var s2 = await AddAsync(a.School2);

        foreach (var table in Tables.Where(t => t.SoftDeletable))
        {
            Guid[] both = [table.Id(s1), table.Id(s2)];
            await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
            {
                db.Remove((await db.FindAsync(table.Type, table.Id(s1)))!);
                await db.SaveChangesAsync();
            }

            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, In(a, a.Root), both)), table.Name);
            Assert.Empty(await VisibleAsync(table, In(a, a.School1), both));
            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, TestCurrentContext.PlatformAdmin(), both)), table.Name + ": admin");
        }
    }

    [Fact]
    public async Task Repather_covers_the_content_tables()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var covered = OrgUnitRepather.DiscoverTargets(db.Model).Select(t => t.ClrType).ToHashSet();

        foreach (var table in Tables)
        {
            Assert.Contains(table.Type, covered);
        }
    }
}
