using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Grading.GradebookTestKit;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 7: auto items made by the grade-event observer, the backfill, the term resolver and the writer rules.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradebookAutoItemTests(LmsApiFactory factory)
{
    private async Task<(World World, Guid Student)> SetupAsync(string code)
    {
        var tree = await AccessTestData.SeedAsync(factory, code);
        var world = await NewWorldAsync(factory, tree.School1);
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        return (world, student.Id);
    }

    private static GradeEventInput Input(World w, Guid student, Guid source, DateTime at, GradeSourceKind kind = GradeSourceKind.Assignment) =>
        new(kind, source, w.ClassCourse.Id, student, 8, 10, at, 1);

    private async Task RecordAsync(GradeEventInput input)
    {
        using var scope = new EngineScope(factory.Services);
        await scope.Services.GetRequiredService<IGradeEvents>().RecordAsync(input, default);
        await scope.Db.SaveChangesAsync();
    }

    private async Task<List<GradeItem>> ItemsAsync(Guid classCourseId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var books = await db.Set<Gradebook>().Where(g => g.ClassCourseId == classCourseId).Select(g => g.Id).ToListAsync();
        return await db.Set<GradeItem>().Where(i => books.Contains(i.GradebookId)).ToListAsync();
    }

    /// <summary>An event stored without the observer, as events were before the gradebook existed.</summary>
    private async Task AddLegacyEventAsync(World w, Guid student, Guid source, DateTime at)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        db.Add(new GradeEvent(cc, Input(w, student, source, at)));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> TermOfAsync(GradeItem item)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<Gradebook>().SingleAsync(g => g.Id == item.GradebookId)).TermId;
    }

    [Fact]
    public async Task Event_creates_its_item_in_the_same_transaction()
    {
        var (w, student) = await SetupAsync("GBA1");
        var source = Guid.NewGuid();
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Services.GetRequiredService<IGradeEvents>().RecordAsync(Input(w, student, source, At(YearStart.AddDays(5))), default);
            // never saved: neither the event nor the item may exist
        }

        Assert.Empty(await ItemsAsync(w.ClassCourse.Id));

        await RecordAsync(Input(w, student, source, At(YearStart.AddDays(5))));
        var item = Assert.Single(await ItemsAsync(w.ClassCourse.Id));
        Assert.Equal(source, item.SourceId);
        Assert.Equal(10, item.MaxPoints);
    }

    [Fact]
    public async Task Auto_item_defaults_to_hidden()
    {
        var (w, student) = await SetupAsync("GBA2");
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddDays(5))));

        Assert.Equal(GradeItem.HiddenState, Assert.Single(await ItemsAsync(w.ClassCourse.Id)).PublishState);
    }

    [Fact]
    public async Task Auto_item_goes_to_uncategorised()
    {
        var (w, student) = await SetupAsync("GBA3");
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddDays(5))));

        var item = Assert.Single(await ItemsAsync(w.ClassCourse.Id));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var category = await db.Set<GradeCategory>().SingleAsync(c => c.Id == item.CategoryId);
        Assert.True(category.IsUncategorised);
        Assert.Equal(0m, category.Weight);
    }

    [Fact]
    public async Task Unknown_source_kind_creates_an_item_with_a_generic_title()
    {
        var (w, student) = await SetupAsync("GBA4");
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddDays(5)), (GradeSourceKind)99));

        var item = Assert.Single(await ItemsAsync(w.ClassCourse.Id));
        Assert.Equal(99, item.SourceKind);
        Assert.Equal("Activity", item.TitleEn);
        Assert.False(string.IsNullOrWhiteSpace(item.TitleAr));
    }

    [Fact]
    public async Task Event_after_term_end_goes_to_the_current_term()
    {
        var (w, student) = await SetupAsync("GBA5");
        // Dated after the last term ended: it lands in the last term, the one in force.
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddMonths(11))));

        Assert.Equal(w.Second.Id, await TermOfAsync(Assert.Single(await ItemsAsync(w.ClassCourse.Id))));
    }

    [Fact]
    public async Task Event_inside_a_term_goes_to_that_term()
    {
        var (w, student) = await SetupAsync("GBA6");
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddDays(10))));

        Assert.Equal(w.First.Id, await TermOfAsync(Assert.Single(await ItemsAsync(w.ClassCourse.Id))));
    }

    [Fact]
    public async Task Term_lookup_never_crosses_clients()
    {
        var (a, _) = await SetupAsync("GBT1A");
        var (b, _) = await SetupAsync("GBT1B");
        using var scope = new EngineScope(factory.Services);
        var resolver = scope.Services.GetRequiredService<EventTermResolver>();
        var cc = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == a.ClassCourse.Id);

        var term = await resolver.ResolveAsync(cc, At(YearStart.AddDays(3)), default);

        Assert.NotNull(term);
        Assert.Equal(a.Year.Id, term!.AcademicYearId);
        Assert.NotEqual(b.Year.Id, term.AcademicYearId);
    }

    [Fact]
    public async Task Backfill_runs_only_for_manage_callers()
    {
        var (w, student) = await SetupAsync("GBB1");
        await AddLegacyEventAsync(w, student, Guid.NewGuid(), At(YearStart.AddDays(5)));

        using (var scope = new EngineScope(factory.Services))
        {
            var cc = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
            Assert.Equal(0, await scope.Services.GetRequiredService<GradebookBackfill>().RunForReaderAsync(cc, false, default));
        }

        Assert.Empty(await ItemsAsync(w.ClassCourse.Id));

        using (var scope = new EngineScope(factory.Services))
        {
            var cc = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
            Assert.Equal(1, await scope.Services.GetRequiredService<GradebookBackfill>().RunForReaderAsync(cc, true, default));
        }

        Assert.Single(await ItemsAsync(w.ClassCourse.Id));
    }

    [Fact]
    public async Task Backfill_never_runs_in_a_closed_year()
    {
        var (w, student) = await SetupAsync("GBB2");
        await AddLegacyEventAsync(w, student, Guid.NewGuid(), At(YearStart.AddDays(5)));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == w.Year.Id)).Close();
            await db.SaveChangesAsync();
        }

        using var scope = new EngineScope(factory.Services);
        var cc = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        Assert.Equal(0, await scope.Services.GetRequiredService<GradebookBackfill>().RunAsync(cc, default));
        Assert.Empty(await ItemsAsync(w.ClassCourse.Id));
    }

    [Fact]
    public async Task Concurrent_backfill_is_idempotent_on_unique_violation()
    {
        var (w, student) = await SetupAsync("GBB3");
        for (var i = 0; i < 3; i++)
        {
            await AddLegacyEventAsync(w, student, Guid.NewGuid(), At(YearStart.AddDays(5 + i)));
        }

        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = new EngineScope(factory.Services);
            var cc = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
            return await scope.Services.GetRequiredService<GradebookBackfill>().RunAsync(cc, default);
        }));

        Assert.Equal(3, (await ItemsAsync(w.ClassCourse.Id)).Count);
        Assert.All(runs, r => Assert.InRange(r, 0, 3));
        using var again = new EngineScope(factory.Services);
        var cc2 = await again.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        Assert.Equal(0, await again.Services.GetRequiredService<GradebookBackfill>().RunAsync(cc2, default));
    }

    [Fact]
    public async Task Student_read_never_creates_rows()
    {
        var (w, student) = await SetupAsync("GBB4");
        await AddLegacyEventAsync(w, student, Guid.NewGuid(), At(YearStart.AddDays(5)));

        using var scope = new EngineScope(factory.Services);
        var cc = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        await scope.Services.GetRequiredService<GradebookBackfill>().RunForReaderAsync(cc, canManage: false, default);
        await scope.Db.SaveChangesAsync();

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<Gradebook>().AnyAsync(g => g.ClassCourseId == w.ClassCourse.Id));
    }

    [Fact]
    public async Task Writer_refuses_closed_class()
    {
        var (w, student) = await SetupAsync("GBW1");
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddDays(5))));
        var item = Assert.Single(await ItemsAsync(w.ClassCourse.Id));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        using var scope = new EngineScope(factory.Services);
        var tracked = await scope.Db.Set<GradeItem>().SingleAsync(i => i.Id == item.Id);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            scope.Services.GetRequiredService<IGradebookWriter>().SetPointsAsync(tracked, student, 5, null, null, default));
    }

    [Fact]
    public async Task Writer_refuses_locked_gradebook_and_stale_version()
    {
        var (w, student) = await SetupAsync("GBW2");
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddDays(5))));
        var seeded = Assert.Single(await ItemsAsync(w.ClassCourse.Id));

        using var scope = new EngineScope(factory.Services);
        var writer = scope.Services.GetRequiredService<IGradebookWriter>();
        var item = await scope.Db.Set<GradeItem>().SingleAsync(i => i.Id == seeded.Id);
        var entry = await writer.SetPointsAsync(item, student, 5, null, null, default);
        await scope.Db.SaveChangesAsync();

        var stale = await Assert.ThrowsAsync<GradebookRuleException>(() => writer.SetPointsAsync(item, student, 6, 0, null, default));
        Assert.Equal("concurrency_conflict", stale.Code);

        var book = await scope.Db.Set<Gradebook>().SingleAsync(g => g.Id == item.GradebookId);
        book.Lock(DateTime.UtcNow);
        var locked = await Assert.ThrowsAsync<GradebookRuleException>(() => writer.SetPointsAsync(item, student, 6, entry.Version, null, default));
        Assert.Equal("gradebook_locked", locked.Code);
    }

    [Fact]
    public async Task Terms_with_a_gradebook_have_dependants()
    {
        var (w, student) = await SetupAsync("GBD1");
        await RecordAsync(Input(w, student, Guid.NewGuid(), At(YearStart.AddDays(5))));

        using var scope = new EngineScope(factory.Services);
        var dependants = scope.Services.GetRequiredService<ITermDependants>();
        Assert.True(await dependants.AnyAsync([w.First.Id], default));
        Assert.False(await dependants.AnyAsync([w.Second.Id], default));
    }
}
