using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Grading;
using static Lms.IntegrationTests.Grading.GradebookTestKit;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 8: the staff grid, configuration, categories and items (FR-GRD-01, FR-GRD-03).</summary>
[Collection(ApiCollection.Name)]
public sealed class GradebookGridTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Grid_returns_server_computed_cells_and_validity()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG1");
        var source = Guid.NewGuid();
        await host.RecordEventAsync(w, w.Students[0].Id, source, 8, 1, At(YearStart.AddDays(3)));

        var grid = await host.GridAsync(w);

        var item = Assert.Single(grid.Items);
        Assert.Equal(PublishStates.Hidden, item.PublishState);
        Assert.Equal("Assignment", item.SourceKind);
        Assert.Equal(grid.Categories.Single(c => c.IsUncategorised).Id, item.CategoryId);
        var row = grid.Rows.Items.Single(r => r.StudentId == w.Students[0].Id);
        var cell = GradebookGridHost.Cell(row, item.Id);
        Assert.Equal((8m, GradeCellStates.Event), (cell.Points, cell.State));
        Assert.Equal(3, grid.Rows.Total);
        Assert.False(grid.IsValid); // only the Uncategorised category with weight 0: the weights do not total 100
        Assert.Equal(0m, grid.WeightsTotal);
        Assert.True(grid.CanManage);
        Assert.Equal(GradeCellStates.Empty, GradebookGridHost.Cell(grid.Rows.Items.First(r => r.StudentId != w.Students[0].Id), item.Id).State);

        // weights that total 100 with the item moved into a category give a valid gradebook and a final
        var categories = await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/categories", w.Gb.First.Id), new SaveGradeCategoriesRequest(
            [.. grid.Categories.Select(c => new SaveGradeCategoryRequest(c.Id, c.NameAr, c.NameEn, c.Weight, c.Order)), new(null, "اختبارات", "Exams", 100, 1)]));
        var exams = (await categories.Content.ReadFromJsonAsync<List<GradeCategoryDto>>())!.Single(c => c.NameEn == "Exams");
        await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/items/" + item.Id), new UpdateGradeItemRequest(exams.Id, item.TitleAr, item.TitleEn, item.MaxPoints, 1));

        var after = await host.GridAsync(w);
        Assert.True(after.IsValid);
        Assert.Equal(80m, after.Rows.Items.Single(r => r.StudentId == w.Students[0].Id).FinalPercent);
        Assert.NotEqual(default, after.ComputedAtUtc);
    }

    [Fact]
    public async Task Grid_pages_students()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG2", students: 3);

        var first = await host.GridAsync(w, query: "pageSize=2&page=1");
        var second = await host.GridAsync(w, query: "pageSize=2&page=2");

        Assert.Equal((3, 2, 1), (first.Rows.Total, first.Rows.Items.Count, first.Rows.Page));
        Assert.Single(second.Rows.Items);
        Assert.Empty(first.Rows.Items.Select(r => r.StudentId).Intersect(second.Rows.Items.Select(r => r.StudentId)));
        Assert.Equal(GradingLimits.GridPageSize, (await host.GridAsync(w)).Rows.PageSize);
        Assert.Equal(GradingLimits.GridPageSizeMax, (await host.GridAsync(w, query: "pageSize=500")).Rows.PageSize);
    }

    [Fact]
    public async Task Former_teacher_gets_404_and_other_teacher_gets_404()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG3");

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Teacher, HttpMethod.Get, host.Url(w, term: w.Gb.First.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.OtherTeacher, HttpMethod.Get, host.Url(w, term: w.Gb.First.Id))).StatusCode);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var assignment = db.Set<Lms.Domain.Teaching.ClassCourseTeacher>().Single(t => t.ClassCourseId == w.Gb.ClassCourse.Id);
            assignment.End(new DateOnly(2021, 1, 1));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.Teacher, HttpMethod.Get, host.Url(w, term: w.Gb.First.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.Teacher, HttpMethod.Put, host.Url(w, "/categories", w.Gb.First.Id),
            new SaveGradeCategoriesRequest([]))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_can_read_but_not_write()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG4");
        await host.GridAsync(w); // the admin's read creates the gradebook

        var read = await host.GridAsync(w, w.Supervisor);
        Assert.False(read.CanManage);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Supervisor, HttpMethod.Put, host.Url(w, "/categories", w.Gb.First.Id), new SaveGradeCategoriesRequest([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Supervisor, HttpMethod.Post, host.Url(w, "/items", w.Gb.First.Id),
            new CreateGradeItemRequest(read.Categories[0].Id, "ا", "A", 10))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PutEntriesAsync(w, w.Supervisor, w.Students[0].Id, 0)).StatusCode);
    }

    [Fact]
    public async Task A_read_without_manage_never_creates_the_gradebook()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG5");

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.Supervisor, HttpMethod.Get, host.Url(w, term: w.Gb.First.Id))).StatusCode);

        await host.GridAsync(w); // the admin creates it
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Supervisor, HttpMethod.Get, host.Url(w, term: w.Gb.First.Id))).StatusCode);
    }

    [Fact]
    public async Task Term_of_another_year_never_resolves()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG6");
        var other = await NewWorldAsync(factory, w.Tree.School1, new DateOnly(2050, 9, 1));

        var response = await host.SendAsync(w.Admin, HttpMethod.Get, host.Url(w, term: other.First.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Config_picks_a_visible_scale_and_validates_the_settings()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG7");
        var scaleResponse = await host.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgUnitGradingScales(w.Tree.School1.Id), new SaveGradingScaleRequest(
            "سلم", "Scale", ScaleKinds.Letter,
            [new(0, 0, 60, "F", "راسب", "Fail", null, null, null), new(1, 60, 100, "P", "ناجح", "Pass", null, null, null)]));
        var scale = (await scaleResponse.Content.ReadFromJsonAsync<GradingScaleDto>())!;
        await host.GridAsync(w);
        var url = host.Url(w, "/config", w.Gb.First.Id);

        var ok = await host.SendAsync(w.Admin, HttpMethod.Put, url, new SaveGradebookConfigRequest(scale.Id, 1, CategoryMethods.PercentAverage, MissingPolicies.Zero));
        var config = (await ok.Content.ReadFromJsonAsync<GradebookConfigDto>())!;
        Assert.Equal((HttpStatusCode.OK, scale.Id, "Scale", 1, CategoryMethods.PercentAverage), (ok.StatusCode, config.ScaleId, config.ScaleNameEn, config.DecimalPlaces, config.CategoryMethod));

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.Admin, HttpMethod.Put, url,
            new SaveGradebookConfigRequest(Guid.NewGuid(), 2, CategoryMethods.PointsWeighted, MissingPolicies.Normalise))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Put, url,
            new SaveGradebookConfigRequest(null, 9, CategoryMethods.PointsWeighted, MissingPolicies.Normalise))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Put, url,
            new SaveGradebookConfigRequest(null, 2, "Median", MissingPolicies.Normalise))).StatusCode);
    }

    [Fact]
    public async Task A_scale_of_another_client_is_404_in_the_config()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG8");
        var b = await host.SeedAsync("GG8B");
        var foreign = await host.SendAsync(b.Admin, HttpMethod.Post, Routes.OrgUnitGradingScales(b.Tree.Root.Id),
            new SaveGradingScaleRequest("س", "S", ScaleKinds.Letter, [new(0, 0, 100, "A", null, null, null, null, null)]));
        var foreignScale = (await foreign.Content.ReadFromJsonAsync<GradingScaleDto>())!;
        await host.GridAsync(w);

        var response = await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/config", w.Gb.First.Id),
            new SaveGradebookConfigRequest(foreignScale.Id, 2, CategoryMethods.PointsWeighted, MissingPolicies.Normalise));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Weights_outside_0_to_100_are_refused_but_a_total_other_than_100_is_only_a_flag()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG9");
        var grid = await host.GridAsync(w);
        var uncategorised = grid.Categories.Single();
        SaveGradeCategoriesRequest Body(decimal weight) => new([
            new(uncategorised.Id, uncategorised.NameAr, uncategorised.NameEn, 0, 0), new(null, "أ", "A", weight, 1)]);
        var url = host.Url(w, "/categories", w.Gb.First.Id);

        var tooBig = await host.SendAsync(w.Admin, HttpMethod.Put, url, Body(101));
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal("weights_invalid", await GradebookGridHost.CodeAsync(tooBig));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Put, url, Body(-1))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Admin, HttpMethod.Put, url, Body(40))).StatusCode);
        var after = await host.GridAsync(w);
        Assert.False(after.IsValid);
        Assert.Equal(40m, after.WeightsTotal);
    }

    [Fact]
    public async Task A_category_with_items_and_the_built_in_one_cannot_be_dropped()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG10");
        var grid = await host.GridAsync(w);
        var uncategorised = grid.Categories.Single();
        var url = host.Url(w, "/categories", w.Gb.First.Id);
        var created = await host.SendAsync(w.Admin, HttpMethod.Put, url, new SaveGradeCategoriesRequest([
            new(uncategorised.Id, uncategorised.NameAr, uncategorised.NameEn, 0, 0), new(null, "أ", "A", 100, 1)]));
        var category = (await created.Content.ReadFromJsonAsync<List<GradeCategoryDto>>())!.Single(c => !c.IsUncategorised);
        await host.AddItemAsync(w, category: category.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Put, url, new SaveGradeCategoriesRequest([
            new(uncategorised.Id, uncategorised.NameAr, uncategorised.NameEn, 0, 0)]))).StatusCode); // A holds an item
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Put, url, new SaveGradeCategoriesRequest([
            new(category.Id, category.NameAr, category.NameEn, 100, 0)]))).StatusCode); // the built-in category stays
    }

    [Fact]
    public async Task Manual_items_are_created_updated_and_deleted_but_event_items_stay()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG11");
        var student = w.Students[0].Id;
        var source = Guid.NewGuid();
        await host.RecordEventAsync(w, student, source, 8, 1, At(YearStart.AddDays(3)));
        var item = await host.AddItemAsync(w, "Quiz", 20);
        var grid = await host.GridAsync(w);
        var eventItem = grid.Items.Single(i => !i.IsManual);
        var category = grid.Categories.Single().Id;
        await host.EntriesAsync(w, student, 0, new GradeEntryChange(item.Id, GradeEntryActions.Set, 15, null));

        var tooLow = await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/items/" + item.Id), new UpdateGradeItemRequest(category, item.TitleAr, item.TitleEn, 10, 5));
        Assert.Equal((HttpStatusCode)422, tooLow.StatusCode);
        Assert.Equal("grade_out_of_range", await GradebookGridHost.CodeAsync(tooLow));
        var renamed = await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/items/" + item.Id), new UpdateGradeItemRequest(category, "جديد", "Renamed", 30, 5));
        var dto = (await renamed.Content.ReadFromJsonAsync<GradeItemDto>())!;
        Assert.Equal(("Renamed", 30m, 5), (dto.TitleEn, dto.MaxPoints, dto.Order));

        // an event item keeps its title and maximum; only its place can change
        var moved = await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/items/" + eventItem.Id), new UpdateGradeItemRequest(category, "x", "x", 99, 9));
        var movedDto = (await moved.Content.ReadFromJsonAsync<GradeItemDto>())!;
        Assert.Equal((eventItem.TitleEn, eventItem.MaxPoints, 9), (movedDto.TitleEn, movedDto.MaxPoints, movedDto.Order));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Delete, host.Url(w, "/items/" + eventItem.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(w.Admin, HttpMethod.Delete, host.Url(w, "/items/" + item.Id))).StatusCode);
        Assert.DoesNotContain(item.Id, (await host.GridAsync(w)).Items.Select(i => i.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.Admin, HttpMethod.Delete, host.Url(w, "/items/" + item.Id))).StatusCode);
    }

    [Fact]
    public async Task An_item_of_another_class_course_is_a_miss()
    {
        using var host = new GradebookGridHost(factory);
        var a = await host.SeedAsync("GG12A");
        var b = await host.SeedAsync("GG12B");
        var foreign = await host.AddItemAsync(b);

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a.Admin, HttpMethod.Delete, host.Url(a, "/items/" + foreign.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a.Admin, HttpMethod.Get, host.Url(b, term: b.Gb.First.Id))).StatusCode); // client B's class course
    }

    [Fact]
    public async Task A_student_who_left_mid_term_stays_visible_to_staff_with_stored_grades_marked_withdrawn()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG14", students: 3);
        var (graded, silent, stays) = (w.Students[0].Id, w.Students[1].Id, w.Students[2].Id);
        var item = await host.AddItemAsync(w, "Paper", 20);
        await host.EntriesAsync(w, graded, 0, new GradeEntryChange(item.Id, GradeEntryActions.Set, 12, null));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            foreach (var e in db.Set<Lms.Domain.Enrollment.CourseEnrollment>().Where(e => e.StudentId == graded || e.StudentId == silent))
            {
                e.Drop(new DateOnly(2026, 10, 5), "left");
            }

            await db.SaveChangesAsync();
        }

        var grid = await host.GridAsync(w);

        Assert.Equal(2, grid.Rows.Total); // the one who left with no stored grades is gone; the one with grades stays
        var left = grid.Rows.Items.Single(r => r.StudentId == graded);
        Assert.True(left.IsWithdrawn);
        Assert.Equal(new DateOnly(2026, 10, 5), left.WithdrawnOn);
        Assert.Equal(12m, GradebookGridHost.Cell(left, item.Id).Points);
        var current = grid.Rows.Items.Single(r => r.StudentId == stays);
        Assert.False(current.IsWithdrawn);
        Assert.Null(current.WithdrawnOn);
        Assert.DoesNotContain(grid.Rows.Items, r => r.StudentId == silent);

        // staff can still correct the stored grades of a student who left
        var corrected = await host.EntriesAsync(w, graded, left.Version, new GradeEntryChange(item.Id, GradeEntryActions.Set, 14, null));
        Assert.True(corrected.IsWithdrawn);
        Assert.Equal(14m, GradebookGridHost.Cell(corrected, item.Id).Points);
    }

    [Fact]
    public async Task Grid_of_40_students_and_60_items_computes_under_budget()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GG13", students: 40);
        var grid = await host.GridAsync(w);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var gradebook = db.Set<Gradebook>().Single(g => g.Id == grid.Id);
            var category = db.Set<GradeCategory>().Single(c => c.GradebookId == grid.Id);
            for (var i = 0; i < 60; i++)
            {
                var item = GradeItem.Manual(gradebook, category, "بند " + i, "Item " + i, 10, i + 1);
                db.Add(item);
                foreach (var student in w.Students.Take(20))
                {
                    db.Add(new GradeEntry(item, student.Id, null, DateTime.UtcNow));
                }
            }

            await db.SaveChangesAsync();
        }

        await host.GridAsync(w); // warm up
        var watch = Stopwatch.StartNew();
        var big = await host.GridAsync(w);
        watch.Stop();

        Assert.Equal((40, 60), (big.Rows.Items.Count, big.Items.Count));
        Assert.True(watch.ElapsedMilliseconds < 3000, $"the grid took {watch.ElapsedMilliseconds} ms");
    }
}
