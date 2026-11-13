using Lms.Domain.Auditing;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Grading;

/// <summary>M4a Task 7 (#357, D51): the append-only grade-event outbox the M5 gradebook reads.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradeEventTests(LmsApiFactory factory)
{
    private static GradeEventInput Input(AcademicRows r, Guid source, int revision, decimal points = 8) =>
        new(GradeSourceKind.Assignment, source, r.ClassCourse.Id, r.Student.Id, points, 10, DateTime.UtcNow, revision);

    private async Task<bool> RecordAsync(GradeEventInput input)
    {
        using var scope = new EngineScope(factory.Services);
        var written = await scope.Services.GetRequiredService<IGradeEvents>().RecordAsync(input, default);
        await scope.Db.SaveChangesAsync();
        return written;
    }

    private async Task<List<GradeEvent>> RowsAsync(Guid source)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<GradeEvent>().Where(e => e.SourceId == source).OrderBy(e => e.Revision).ToListAsync();
    }

    [Fact]
    public async Task Same_revision_twice_writes_once()
    {
        var tree = await AccessTestData.SeedAsync(factory, "GE1");
        var rows = await AcademicTestKit.AddAllAsync(factory, tree.School1);
        var source = Guid.NewGuid();

        Assert.True(await RecordAsync(Input(rows, source, 1)));
        Assert.False(await RecordAsync(Input(rows, source, 1, points: 3)));

        var stored = Assert.Single(await RowsAsync(source));
        Assert.Equal(8, stored.Points);
    }

    [Fact]
    public async Task Same_revision_twice_in_one_unit_of_work_writes_once()
    {
        var tree = await AccessTestData.SeedAsync(factory, "GE2");
        var rows = await AcademicTestKit.AddAllAsync(factory, tree.School1);
        var source = Guid.NewGuid();

        using var scope = new EngineScope(factory.Services);
        var events = scope.Services.GetRequiredService<IGradeEvents>();
        Assert.True(await events.RecordAsync(Input(rows, source, 1), default));
        Assert.False(await events.RecordAsync(Input(rows, source, 1), default));
        await scope.Db.SaveChangesAsync();

        Assert.Single(await RowsAsync(source));
    }

    [Fact]
    public async Task Regrade_writes_next_revision()
    {
        var tree = await AccessTestData.SeedAsync(factory, "GE3");
        var rows = await AcademicTestKit.AddAllAsync(factory, tree.School1);
        var source = Guid.NewGuid();

        Assert.True(await RecordAsync(Input(rows, source, 1, points: 5)));
        Assert.True(await RecordAsync(Input(rows, source, 2, points: 9)));

        var stored = await RowsAsync(source);
        Assert.Equal([1, 2], stored.Select(e => e.Revision));
        Assert.Equal([5m, 9m], stored.Select(e => e.Points));
        Assert.All(stored, e => Assert.Equal(10, e.MaxPoints));
    }

    [Fact]
    public async Task Another_source_kind_or_student_with_the_same_revision_is_a_separate_event()
    {
        var tree = await AccessTestData.SeedAsync(factory, "GE4");
        var rows = await AcademicTestKit.AddAllAsync(factory, tree.School1);
        var source = Guid.NewGuid();

        Assert.True(await RecordAsync(Input(rows, source, 1)));
        Assert.True(await RecordAsync(Input(rows, source, 1) with { SourceKind = GradeSourceKind.Quiz }));

        Assert.Equal(2, (await RowsAsync(source)).Count);
    }

    [Fact]
    public async Task GradeEvent_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "GE5A");
        var b = await AccessTestData.SeedAsync(factory, "GE5B");
        var rows = await AcademicTestKit.AddAllAsync(factory, a.School1);
        var source = Guid.NewGuid();
        await RecordAsync(Input(rows, source, 1));

        var stored = Assert.Single(await RowsAsync(source));
        Assert.Equal(rows.ClassCourse.OrganizationId, stored.OrganizationId);
        Assert.Equal(rows.ClassCourse.OrgUnitId, stored.OrgUnitId);
        Assert.Equal(rows.ClassCourse.OrgUnitPath, stored.OrgUnitPath);

        async Task<int> SeenAsync(TestCurrentContext who)
        {
            await using var db = factory.CreateDbContext(who);
            return await db.Set<GradeEvent>().CountAsync(e => e.SourceId == source);
        }

        Assert.Equal(1, await SeenAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path)));
        Assert.Equal(1, await SeenAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path)));
        Assert.Equal(0, await SeenAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path)));
        Assert.Equal(0, await SeenAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path)));
        Assert.Equal(0, await SeenAsync(TestCurrentContext.For(a.Org.Id, null)));
        // A scope below the event's unit (an ancestor-only row is invisible to it) and another client's scope on a path in our tree.
        Assert.Equal(0, await SeenAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path + Guid.NewGuid() + "/")));
        Assert.Equal(0, await SeenAsync(TestCurrentContext.For(b.Org.Id, a.School1.Path)));
        Assert.Equal(0, await SeenAsync(TestCurrentContext.For(b.Org.Id, a.Root.Path)));
        Assert.Equal(0, await SeenAsync(new TestCurrentContext { UserId = Guid.NewGuid() }));

        await using var audit = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await audit.AuditEntries.AnyAsync(e => e.EntityType == nameof(GradeEvent) && e.EntityId == stored.Id && e.Action == AuditAction.Created));
    }

    [Fact]
    public async Task Quiz_scores_write_the_next_revision_only_when_the_score_changes()
    {
        var tree = await AccessTestData.SeedAsync(factory, "GE6");
        var rows = await AcademicTestKit.AddAllAsync(factory, tree.School1);
        var quiz = Guid.NewGuid();

        async Task<bool> ScoreAsync(decimal points)
        {
            using var scope = new EngineScope(factory.Services);
            var written = await scope.Services.GetRequiredService<IGradeEvents>().RecordScoreAsync(
                GradeSourceKind.Quiz, quiz, rows.ClassCourse.Id, rows.Student.Id, points, 10, DateTime.UtcNow, default);
            await scope.Db.SaveChangesAsync();
            return written;
        }

        Assert.True(await ScoreAsync(6));
        Assert.False(await ScoreAsync(6)); // a second attempt that leaves the final score alone
        Assert.True(await ScoreAsync(9)); // a better attempt
        Assert.True(await ScoreAsync(7)); // a regrade that lowers it

        var stored = await RowsAsync(quiz);
        Assert.Equal([1, 2, 3], stored.Select(e => e.Revision));
        Assert.Equal([6m, 9m, 7m], stored.Select(e => e.Points));
        Assert.All(stored, e => Assert.Equal(GradeSourceKind.Quiz, e.SourceKind));
    }
}
