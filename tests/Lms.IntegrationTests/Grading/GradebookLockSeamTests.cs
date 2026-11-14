using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 12 (L5): a locked gradebook refuses writes, ignores later events and keeps its results; the lock itself arrives in M7.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradebookLockSeamTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private async Task<StudentCourseGradesDto> StudentViewAsync(GradeWorld g)
    {
        var response = await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentGradesDto>())!.Courses.Single();
    }

    private async Task RecordEventAsync(GradeWorld g, Guid source, decimal points, int revision)
    {
        using var scope = new EngineScope(factory.Services);
        await scope.Services.GetRequiredService<IGradeEvents>().RecordAsync(
            new GradeEventInput(GradeSourceKind.Assignment, source, g.W.ClassCourse.Id, g.W.StudentProfileId, points, 10, DateTime.UtcNow, revision), default);
        await scope.Db.SaveChangesAsync();
    }

    /// <summary>An event source whose item is published, so the student sees its points.</summary>
    private async Task<Guid> PublishedSourceAsync(GradeWorld g, decimal points)
    {
        var source = Guid.NewGuid();
        await RecordEventAsync(g, source, points, 1);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.Set<GradeItem>().SingleAsync(i => i.SourceId == source)).Publish(DateTime.UtcNow);
        await db.SaveChangesAsync();
        return source;
    }

    [Fact]
    public async Task Locked_gradebook_refuses_every_write_with_gradebook_locked()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "LK1");
        await TestLock.LockAsync(factory, g.Book.Id);
        var cc = g.W.ClassCourse.Id;

        var responses = new[]
        {
            await _t.SendAsync(g.W.Teacher, HttpMethod.Put, Routes.ClassCourseGradebookConfig(cc, g.Term.Id), new SaveGradebookConfigRequest(null, 2, "PointsWeighted", "Normalise")),
            await _t.SendAsync(g.W.Teacher, HttpMethod.Put, Routes.ClassCourseGradebookCategories(cc, g.Term.Id), new SaveGradeCategoriesRequest([new SaveGradeCategoryRequest(g.Tests.Id, "اختبارات", "Tests", 100m, 1)])),
            await _t.SendAsync(g.W.Teacher, HttpMethod.Post, Routes.ClassCourseGradebookItems(cc, g.Term.Id), new CreateGradeItemRequest(g.Tests.Id, "ورقة", "Paper", 10m)),
            await _t.SendAsync(g.W.Teacher, HttpMethod.Put, Routes.ClassCourseGradebookEntries(cc, g.Term.Id),
                new UpsertEntriesRequest(g.W.StudentProfileId, 0, [new GradeEntryChange(g.Published.Id, GradeEntryActions.Set, 5m, null)])),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        foreach (var response in responses)
        {
            Assert.Equal(ProblemCodes.gradebook_locked, await ContentTestHost.CodeAsync(response));
        }
    }

    [Fact]
    public async Task Event_recorded_after_lock_does_not_change_locked_results()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "LK3");
        var source = await PublishedSourceAsync(g, 4m);
        var before = await StudentViewAsync(g);
        Assert.Contains(before.Items, i => i.Points == 4m);

        await TestLock.LockAsync(factory, g.Book.Id);
        await Task.Delay(50);
        await RecordEventAsync(g, source, 9m, 2); // a regrade after the lock

        var after = await StudentViewAsync(g);
        Assert.Equal(before.FinalPercent, after.FinalPercent);
        Assert.Equal(before.Items.Select(i => (i.ItemId, i.Points)), after.Items.Select(i => (i.ItemId, i.Points)));
        Assert.Equal(before.Categories.Select(c => c.Percent), after.Categories.Select(c => c.Percent));
    }

    [Fact]
    public async Task Events_after_lock_are_counted_for_staff_only()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "LK4");
        var source = await PublishedSourceAsync(g, 4m);
        await TestLock.LockAsync(factory, g.Book.Id);
        await Task.Delay(50);
        await RecordEventAsync(g, source, 9m, 2);

        var grid = await _t.SendAsync(g.W.Teacher, HttpMethod.Get, Routes.ClassCourseGradebook(g.W.ClassCourse.Id, g.Term.Id));
        Assert.Equal(HttpStatusCode.OK, grid.StatusCode);
        var dto = (await grid.Content.ReadFromJsonAsync<GradebookDto>())!;
        Assert.Equal("Locked", dto.State);
        Assert.Equal(1, dto.EventsAfterLock);

        var studentBody = await (await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("afterlock", studentBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked", studentBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Staff_grid_keeps_its_values_after_lock_too()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "LK5");
        var source = await PublishedSourceAsync(g, 4m);
        async Task<decimal?> FinalAsync()
        {
            var grid = await _t.SendAsync(g.W.Teacher, HttpMethod.Get, Routes.ClassCourseGradebook(g.W.ClassCourse.Id, g.Term.Id));
            return (await grid.Content.ReadFromJsonAsync<GradebookDto>())!.Rows.Items.Single().FinalPercent;
        }

        var before = await FinalAsync();
        await TestLock.LockAsync(factory, g.Book.Id);
        await Task.Delay(50);
        await RecordEventAsync(g, source, 10m, 2);

        Assert.Equal(before, await FinalAsync());
    }

    [Fact]
    public async Task A_new_source_after_lock_creates_no_item_and_a_regrade_keeps_the_item_maximum()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "LK6");
        var existing = await PublishedSourceAsync(g, 4m);
        await TestLock.LockAsync(factory, g.Book.Id);
        await Task.Delay(50);

        await RecordEventAsync(g, Guid.NewGuid(), 5m, 1);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Services.GetRequiredService<IGradeEvents>().RecordAsync(
                new GradeEventInput(GradeSourceKind.Assignment, existing, g.W.ClassCourse.Id, g.W.StudentProfileId, 15, 20, DateTime.UtcNow, 2), default);
            await scope.Db.SaveChangesAsync();
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var items = await db.Set<GradeItem>().Where(i => i.GradebookId == g.Book.Id && i.SourceId != null).ToListAsync();
        Assert.Single(items);
        Assert.Equal(10m, items[0].MaxPoints);
    }
}
