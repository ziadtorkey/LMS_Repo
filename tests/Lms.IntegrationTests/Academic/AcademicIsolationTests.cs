using System.Reflection;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Academic;

/// <summary>
/// Table-level isolation of every academic table (M3 Task 5, PRD 4.2): another client sees nothing, a sibling unit sees
/// nothing, a parent sees its descendants, no scope sees nothing, soft-deleted rows are hidden from everyone.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AcademicIsolationTests(LmsApiFactory factory)
{
    private sealed record Table(string Name, Type Type, Func<AcademicRows, Guid> Id, bool SoftDeletable);

    private static readonly Table[] Tables =
    [
        new("Course", typeof(Course), r => r.Course.Id, true),
        new("CourseAllocation", typeof(CourseAllocation), r => r.Allocation.Id, true),
        new("CourseRestriction", typeof(CourseRestriction), r => r.Restriction.Id, true),
        new("CourseSupervisor", typeof(CourseSupervisor), r => r.Supervisor.Id, true),
        new("Class", typeof(Class), r => r.Class.Id, true),
        new("ClassStudent", typeof(ClassStudent), r => r.Placement.Id, true),
        new("ClassCourse", typeof(ClassCourse), r => r.ClassCourse.Id, true),
        new("TeacherCourseEligibility", typeof(TeacherCourseEligibility), r => r.Eligibility.Id, true),
        new("ClassCourseTeacher", typeof(ClassCourseTeacher), r => r.Teacher.Id, true),
        new("CourseEnrollment", typeof(CourseEnrollment), r => r.Enrollment.Id, true),
        new("StudentGroup", typeof(StudentGroup), r => r.Group.Id, true),
        new("GroupMember", typeof(GroupMember), r => r.Member.Id, true),
        new("StudentTransfer", typeof(StudentTransfer), r => r.Transfer.Id, false),
    ];

    private static TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

    private async Task<List<Guid>> VisibleAsync(Table table, TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var method = typeof(AcademicIsolationTests).GetMethod(nameof(IdsOf), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(table.Type);
        var ids = await (Task<List<Guid>>)method.Invoke(null, [db])!;
        return ids.Where(among.Contains).Order().ToList();
    }

    private static Task<List<Guid>> IdsOf<T>(LmsDbContext db) where T : class =>
        db.Set<T>().Select(e => EF.Property<Guid>(e, "Id")).ToListAsync();

    [Fact]
    public async Task Every_academic_table_is_isolated_by_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "AI1A");
        var b = await AccessTestData.SeedAsync(factory, "AI1B");
        var s1 = await AcademicTestKit.AddAllAsync(factory, a.School1);
        var s2 = await AcademicTestKit.AddAllAsync(factory, a.School2);
        var other = await AcademicTestKit.AddAllAsync(factory, b.Root);
        var atRoot = await AcademicTestKit.AddAllAsync(factory, a.Root);

        foreach (var table in Tables)
        {
            Guid[] all = [table.Id(s1), table.Id(s2), table.Id(other), table.Id(atRoot)];

            // another client sees only its own; siblings only theirs; a parent its own row and its descendants
            Assert.True(new[] { table.Id(other) }.SequenceEqual(await VisibleAsync(table, In(b, b.Root), all)), table.Name + ": other client");
            Assert.True(new[] { table.Id(s1) }.SequenceEqual(await VisibleAsync(table, In(a, a.School1), all)), table.Name + ": school 1");
            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, In(a, a.School2), all)), table.Name + ": school 2");
            Assert.True(
                new[] { table.Id(s1), table.Id(s2), table.Id(atRoot) }.Order().SequenceEqual(await VisibleAsync(table, In(a, a.Root), all)),
                table.Name + ": parent");
            // no scope, no client, malformed scope: nothing
            Assert.Empty(await VisibleAsync(table, TestCurrentContext.For(a.Org.Id, null), all));
            Assert.Empty(await VisibleAsync(table, new TestCurrentContext { UserId = Guid.NewGuid() }, all));
            Assert.Empty(await VisibleAsync(table, TestCurrentContext.For(a.Org.Id, a.School1.Path[..^1]), all));
            // platform admin sees every client
            Assert.True(all.Order().SequenceEqual(await VisibleAsync(table, TestCurrentContext.PlatformAdmin(), all)), table.Name + ": platform admin");
        }
    }

    [Fact]
    public async Task Soft_deleted_academic_rows_disappear_for_everyone()
    {
        var a = await AccessTestData.SeedAsync(factory, "AI2");
        var s1 = await AcademicTestKit.AddAllAsync(factory, a.School1);
        var s2 = await AcademicTestKit.AddAllAsync(factory, a.School2);

        foreach (var table in Tables.Where(t => t.SoftDeletable))
        {
            Guid[] both = [table.Id(s1), table.Id(s2)];
            await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
            {
                var entity = await db.FindAsync(table.Type, table.Id(s1));
                db.Remove(entity!);
                await db.SaveChangesAsync();
            }

            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, In(a, a.Root), both)), table.Name);
            Assert.Empty(await VisibleAsync(table, In(a, a.School1), both));
            Assert.True(new[] { table.Id(s2) }.SequenceEqual(await VisibleAsync(table, TestCurrentContext.PlatformAdmin(), both)), table.Name + ": admin");
        }
    }

    [Fact]
    public async Task Repather_covers_academic_tables()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var covered = OrgUnitRepather.DiscoverTargets(db.Model).Select(t => t.ClrType).ToHashSet();

        foreach (var table in Tables)
        {
            Assert.Contains(table.Type, covered);
        }
    }
}
