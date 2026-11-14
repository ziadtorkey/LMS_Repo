using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Grading.GradebookGridHost;
using static Lms.IntegrationTests.Grading.GradebookTestKit;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 8: entries of a student's row (L12): manual scores, overrides, excuses, keep or drop, concurrency.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradeEntryTests(LmsApiFactory factory)
{
    private static GradeEntryChange Set(Guid item, decimal? points) => new(item, GradeEntryActions.Set, points, null);

    private static GradeEntryChange Override(Guid item, decimal points, string? reason = "Re-marked by hand") =>
        new(item, GradeEntryActions.Override, points, reason);

    private static GradeEntryChange Act(Guid item, string action) => new(item, action, null, null);

    /// <summary>One assignment event item (max 10) and one manual item (max 20), both in a 100-weight category.</summary>
    private async Task<(GradebookGridHost Host, GradebookGridHost.World W, Guid Student, Guid EventItem, Guid Manual, Guid Source)> SetupAsync(string name)
    {
        var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync(name);
        var student = w.Students[0].Id;
        var source = Guid.NewGuid();
        await host.RecordEventAsync(w, student, source, 6, 1, At(YearStart.AddDays(3)));
        var manual = await host.AddItemAsync(w, "Paper", 20);
        var grid = await host.GridAsync(w);
        return (host, w, student, grid.Items.Single(i => !i.IsManual).Id, manual.Id, source);
    }

    private static GradeRowDto RowOf(GradebookDto grid, Guid student) => grid.Rows.Items.Single(r => r.StudentId == student);

    [Fact]
    public async Task Entry_write_returns_the_recomputed_row()
    {
        var (host, w, student, eventItem, manual, _) = await SetupAsync("GE1");
        using var _h = host;
        var categories = (await host.GridAsync(w)).Categories;
        var saved = await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/categories", w.Gb.First.Id), new SaveGradeCategoriesRequest(
            [.. categories.Select(c => new SaveGradeCategoryRequest(c.Id, c.NameAr, c.NameEn, c.Weight, c.Order)), new(null, "اختبارات", "Exams", 100, 1)]));
        var exams = (await saved.Content.ReadFromJsonAsync<List<GradeCategoryDto>>())!.Single(c => c.NameEn == "Exams");
        foreach (var id in new[] { eventItem, manual })
        {
            var item = (await host.GridAsync(w)).Items.Single(i => i.Id == id);
            await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/items/" + id), new UpdateGradeItemRequest(exams.Id, item.TitleAr, item.TitleEn, item.MaxPoints, item.Order));
        }

        var row = await host.EntriesAsync(w, student, 0, Set(manual, 15));

        Assert.Equal((15m, GradeCellStates.Manual), (Cell(row, manual).Points, Cell(row, manual).State));
        Assert.Equal((6m, GradeCellStates.Event), (Cell(row, eventItem).Points, Cell(row, eventItem).State));
        Assert.Equal(70m, row.FinalPercent); // (6 + 15) / (10 + 20) points
        Assert.True(row.Version > 0);
        Assert.Equal(row.FinalPercent, RowOf(await host.GridAsync(w), student).FinalPercent);
    }

    [Fact]
    public async Task Event_scores_are_read_only_and_override_needs_an_event_item()
    {
        var (host, w, student, eventItem, manual, _) = await SetupAsync("GE2");
        using var _h = host;

        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutEntriesAsync(w, w.Admin, student, 0, Set(eventItem, 9))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutEntriesAsync(w, w.Admin, student, 0, Override(manual, 9))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutEntriesAsync(w, w.Admin, student, 0, Act(eventItem, "Wipe"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.PutEntriesAsync(w, w.Admin, student, 0, Set(Guid.NewGuid(), 1))).StatusCode);
        Assert.Equal((6m, GradeCellStates.Event), (Cell(RowOf(await host.GridAsync(w), student), eventItem).Points, GradeCellStates.Event));
    }

    [Fact]
    public async Task Override_requires_a_reason_and_is_audited()
    {
        var (host, w, student, eventItem, _, _) = await SetupAsync("GE3");
        using var _h = host;

        var noReason = await host.PutEntriesAsync(w, w.Admin, student, 0, Override(eventItem, 9, reason: " "));
        Assert.Equal((HttpStatusCode)422, noReason.StatusCode);
        Assert.Equal("grade_override_reason_required", await CodeAsync(noReason));
        var longReason = await host.PutEntriesAsync(w, w.Admin, student, 0, Override(eventItem, 9, new string('x', 501)));
        Assert.Equal("grade_override_reason_required", await CodeAsync(longReason));

        var row = await host.EntriesAsync(w, student, 0, Override(eventItem, 9, "Re-marked by hand"));

        Assert.Equal((9m, GradeCellStates.Override, "Re-marked by hand"), (Cell(row, eventItem).Points, Cell(row, eventItem).State, Cell(row, eventItem).Reason));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.Set<GradeEntry>().SingleAsync(e => e.GradeItemId == eventItem && e.StudentProfileId == student);
        Assert.Equal(("Re-marked by hand", true), (entry.Reason, entry.IsOverride));
        Assert.NotNull(entry.ByUserId); // who and when are on the row; entry changes are [Audited] like every sensitive entity
        Assert.True(typeof(GradeEntry).GetCustomAttributes(typeof(Lms.Domain.Auditing.AuditedAttribute), false).Length == 1);
    }

    [Fact]
    public async Task Regrade_after_override_shows_newer_score_exists()
    {
        var (host, w, student, eventItem, _, source) = await SetupAsync("GE4");
        using var _h = host;
        var overridden = await host.EntriesAsync(w, student, 0, Override(eventItem, 9));
        Assert.Equal(GradeCellStates.Override, Cell(overridden, eventItem).State);

        await host.RecordEventAsync(w, student, source, 4, 2, DateTime.UtcNow.AddMinutes(5)); // a regrade after the override

        var cell = Cell(RowOf(await host.GridAsync(w), student), eventItem);
        Assert.Equal((GradeCellStates.NewerScoreExists, 9m), (cell.State, cell.Points)); // the override still counts until staff decide
    }

    [Fact]
    public async Task Keep_or_drop_override()
    {
        var (host, w, student, eventItem, _, source) = await SetupAsync("GE5");
        using var _h = host;
        var row = await host.EntriesAsync(w, student, 0, Override(eventItem, 9));
        await host.RecordEventAsync(w, student, source, 4, 2, DateTime.UtcNow.AddMinutes(5));
        row = RowOf(await host.GridAsync(w), student);

        var kept = await host.EntriesAsync(w, student, row.Version, Act(eventItem, GradeEntryActions.KeepOverride));
        Assert.Equal((GradeCellStates.Override, 9m), (Cell(kept, eventItem).State, Cell(kept, eventItem).Points)); // the marker is gone, the override stays

        await host.RecordEventAsync(w, student, source, 3, 3, DateTime.UtcNow.AddMinutes(10));
        row = RowOf(await host.GridAsync(w), student);
        Assert.Equal(GradeCellStates.NewerScoreExists, Cell(row, eventItem).State);
        var dropped = await host.EntriesAsync(w, student, row.Version, Act(eventItem, GradeEntryActions.DropOverride));
        Assert.Equal((GradeCellStates.Event, 3m), (Cell(dropped, eventItem).State, Cell(dropped, eventItem).Points)); // the newest event score counts

        var notAnOverride = await host.PutEntriesAsync(w, w.Admin, student, dropped.Version, Act(eventItem, GradeEntryActions.KeepOverride));
        Assert.Equal(HttpStatusCode.BadRequest, notAnOverride.StatusCode);
    }

    [Fact]
    public async Task Clear_override_and_excuse_and_clear()
    {
        var (host, w, student, eventItem, manual, _) = await SetupAsync("GE6");
        using var _h = host;
        var row = await host.EntriesAsync(w, student, 0, Override(eventItem, 9), Set(manual, 12));

        row = await host.EntriesAsync(w, student, row.Version, Act(eventItem, GradeEntryActions.ClearOverride), Act(manual, GradeEntryActions.Excuse));
        Assert.Equal((GradeCellStates.Event, 6m), (Cell(row, eventItem).State, Cell(row, eventItem).Points));
        Assert.Equal((GradeCellStates.Excused, (decimal?)null), (Cell(row, manual).State, Cell(row, manual).Points));

        row = await host.EntriesAsync(w, student, row.Version, Act(manual, GradeEntryActions.Clear));
        Assert.Equal(GradeCellStates.Empty, Cell(row, manual).State);
        row = await host.EntriesAsync(w, student, row.Version, Set(manual, 5));
        row = await host.EntriesAsync(w, student, row.Version, Act(manual, GradeEntryActions.Clear));
        Assert.Equal(GradeCellStates.Empty, Cell(row, manual).State);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(20.5)]
    public async Task Out_of_range_points_are_422(double points)
    {
        var (host, w, student, _, manual, _) = await SetupAsync("GE7");
        using var _h = host;

        var response = await host.PutEntriesAsync(w, w.Admin, student, 0, Set(manual, (decimal)points));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("grade_out_of_range", await CodeAsync(response));
        Assert.Equal(GradeCellStates.Empty, Cell(RowOf(await host.GridAsync(w), student), manual).State);
        // the boundaries are valid
        Assert.Equal(20m, Cell(await host.EntriesAsync(w, student, 0, Set(manual, 20)), manual).Points);
    }

    [Fact]
    public async Task A_bad_change_in_a_batch_writes_nothing()
    {
        var (host, w, student, _, manual, _) = await SetupAsync("GE8");
        using var _h = host;

        var response = await host.PutEntriesAsync(w, w.Admin, student, 0, Set(manual, 5), Set(manual, 99));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal(GradeCellStates.Empty, Cell(RowOf(await host.GridAsync(w), student), manual).State);
    }

    [Fact]
    public async Task Stale_version_is_409_concurrency_conflict_with_the_current_row()
    {
        var (host, w, student, _, manual, _) = await SetupAsync("GE9");
        using var _h = host;
        var first = await host.EntriesAsync(w, student, 0, Set(manual, 10));
        await host.EntriesAsync(w, student, first.Version, Set(manual, 12)); // another teacher saves first

        var stale = await host.PutEntriesAsync(w, w.Admin, student, first.Version, Set(manual, 3));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("concurrency_conflict", body.GetProperty("code").GetString());
        Assert.Equal(12m, body.GetProperty("extra").GetProperty("cells").EnumerateArray()
            .Single(c => c.GetProperty("itemId").GetGuid() == manual).GetProperty("points").GetDecimal());
        Assert.Equal(12m, Cell(RowOf(await host.GridAsync(w), student), manual).Points);
    }

    [Fact]
    public async Task A_student_who_is_not_enrolled_is_404()
    {
        var (host, w, _, _, manual, _) = await SetupAsync("GE10");
        using var _h = host;
        var outsider = await Lms.IntegrationTests.People.PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, Lms.IntegrationTests.People.PeopleTestKit.NewCode());

        Assert.Equal(HttpStatusCode.NotFound, (await host.PutEntriesAsync(w, w.Admin, outsider.Id, 0, Set(manual, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.PutEntriesAsync(w, w.Admin, Guid.NewGuid(), 0, Set(manual, 1))).StatusCode);
    }

    [Fact]
    public async Task A_locked_gradebook_refuses_every_write()
    {
        var (host, w, student, _, manual, _) = await SetupAsync("GE11");
        using var _h = host;
        var grid = await host.GridAsync(w);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Gradebook>().SingleAsync(g => g.Id == grid.Id)).Lock(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var entries = await host.PutEntriesAsync(w, w.Admin, student, 0, Set(manual, 5));
        Assert.Equal((HttpStatusCode.Conflict, "gradebook_locked"), (entries.StatusCode, await CodeAsync(entries)));
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(w.Admin, HttpMethod.Post, host.Url(w, "/items", w.Gb.First.Id),
            new CreateGradeItemRequest(grid.Categories[0].Id, "ا", "A", 5))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/categories", w.Gb.First.Id),
            new SaveGradeCategoriesRequest([]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Admin, HttpMethod.Get, host.Url(w, term: w.Gb.First.Id))).StatusCode); // reads still work
    }

    [Fact]
    public async Task Invalid_bodies_are_400()
    {
        var (host, w, student, _, manual, _) = await SetupAsync("GE12");
        using var _h = host;

        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutEntriesAsync(w, w.Admin, student, 0)).StatusCode); // no changes
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutEntriesAsync(w, w.Admin, student, -1, Set(manual, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Post, host.Url(w, "/items", w.Gb.First.Id),
            new CreateGradeItemRequest(Guid.NewGuid(), "ا", "A", 0))).StatusCode);
    }
}
