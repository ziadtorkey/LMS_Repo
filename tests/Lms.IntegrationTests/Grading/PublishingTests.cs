using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Grading.GradebookGridHost;
using static Lms.IntegrationTests.Grading.GradebookTestKit;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 11: publishing (now, schedule, hide), snapshots, the provisional-scale rule and the notice (FR-GRD-05, L6, L7, L26).</summary>
[Collection(ApiCollection.Name)]
public sealed class PublishingTests(LmsApiFactory factory)
{
    private sealed record Setup(
        GradebookGridHost Host, GradebookGridHost.World W, GradeItemDto A, GradeItemDto B, Guid StudentUser, Guid GuardianUser);

    /// <summary>Three students (the first with an account and a guardian account), two manual items; student 0 graded on both, student 1 on A only.</summary>
    private async Task<Setup> SetupAsync(string name)
    {
        var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync(name);
        var studentUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardianUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var student = await db.Set<StudentProfile>().SingleAsync(s => s.Id == w.Students[0].Id);
            student.LinkUser(studentUser);
            var guardian = await PeopleTestKit.AddGuardianAsync(factory, w.Tree.Org.Id, PeopleTestKit.NewMobile(), userId: guardianUser);
            db.Set<StudentGuardian>().Add(StudentGuardian.For(student, await db.Set<GuardianProfile>().SingleAsync(g => g.Id == guardian.Id), "Father", true));
            await db.SaveChangesAsync();
        }

        var a = await host.AddItemAsync(w, "Quiz A", 10);
        var b = await host.AddItemAsync(w, "Quiz B", 20);
        var row = await host.EntriesAsync(w, w.Students[0].Id, 0, new GradeEntryChange(a.Id, GradeEntryActions.Set, 8, null));
        await host.EntriesAsync(w, w.Students[0].Id, row.Version, new GradeEntryChange(b.Id, GradeEntryActions.Set, 15, null));
        await host.EntriesAsync(w, w.Students[1].Id, 0, new GradeEntryChange(a.Id, GradeEntryActions.Set, 5, null));
        return new Setup(host, w, a, b, studentUser, guardianUser);
    }

    private static HttpMethod Post => HttpMethod.Post;

    private Task<HttpResponseMessage> PublishAsync(Setup s, PublishItemsRequest request, Lms.Domain.Access.RoleAssignment? caller = null) =>
        s.Host.SendAsync(caller ?? s.W.Admin, Post, Routes.ClassCourseGradebookPublish(s.W.Gb.ClassCourse.Id), request);

    private static PublishItemsRequest Now(params Guid[] ids) => new(ids, PublishModes.Now, null, false);

    private async Task<List<GradeItem>> ItemsAsync(params Guid[] ids)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<GradeItem>().Where(i => ids.Contains(i.Id)).ToListAsync();
    }

    private async Task<List<Notification>> NoticesAsync(Guid user)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().Where(n => n.UserId == user && n.EventKey == "Grades.Published").ToListAsync();
    }

    [Fact]
    public async Task Publish_now_publishes_the_items_and_counts_the_effect()
    {
        var s = await SetupAsync("PB1");
        using var _h = s.Host;

        var response = await PublishAsync(s, Now(s.A.Id, s.B.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PublishResultDto>())!;
        Assert.Equal((2, 2, 1), (result.ItemsChanged, result.AffectedStudents, result.NotificationsQueued)); // 2 students have scores; only student 0 has an account
        Assert.All(await ItemsAsync(s.A.Id, s.B.Id), i => Assert.Equal((GradeItem.PublishedState, true), (i.PublishState, i.PublishedAtUtc is not null)));

        var again = (await (await PublishAsync(s, Now(s.A.Id, s.B.Id))).Content.ReadFromJsonAsync<PublishResultDto>())!;
        Assert.Equal((0, 0, 0), (again.ItemsChanged, again.AffectedStudents, again.NotificationsQueued)); // nothing changed, nobody is told twice
    }

    [Fact]
    public async Task Schedule_in_the_past_is_refused_and_a_future_schedule_waits()
    {
        var s = await SetupAsync("PB2");
        using var _h = s.Host;

        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(s, new(new[] { s.A.Id }, PublishModes.Schedule, DateTime.UtcNow.AddMinutes(-1), false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(s, new(new[] { s.A.Id }, PublishModes.Schedule, null, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(s, new(new[] { s.A.Id }, "Whenever", null, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(s, Now())).StatusCode);

        var at = DateTime.UtcNow.AddHours(2);
        var ok = (await (await PublishAsync(s, new(new[] { s.A.Id }, PublishModes.Schedule, at, false))).Content.ReadFromJsonAsync<PublishResultDto>())!;
        Assert.Equal((1, 0, 0), (ok.ItemsChanged, ok.AffectedStudents, ok.NotificationsQueued)); // no snapshot and no notice until it is published
        var item = Assert.Single(await ItemsAsync(s.A.Id));
        Assert.Equal((GradeItem.ScheduledState, true), (item.PublishState, item.PublishAtUtc is not null));
        Assert.Empty(await NoticesAsync(s.StudentUser));
    }

    [Fact]
    public async Task Scheduled_item_is_published_by_the_job_once()
    {
        var s = await SetupAsync("PB3");
        using var _h = s.Host;
        await PublishAsync(s, new(new[] { s.A.Id }, PublishModes.Schedule, DateTime.UtcNow.AddMinutes(30), false));

        using (var early = new EngineScope(s.Host.Host.Services))
        {
            Assert.Equal(0, await early.Services.GetRequiredService<Lms.Infrastructure.Grading.Publishing.ScheduledPublishJob>()
                .ProcessAsync(s.W.Tree.Org.Id, DateTime.UtcNow, default)); // not due yet
        }

        var later = DateTime.UtcNow.AddHours(1);
        using (var scope = new EngineScope(s.Host.Host.Services))
        {
            var job = scope.Services.GetRequiredService<Lms.Infrastructure.Grading.Publishing.ScheduledPublishJob>();
            Assert.Equal(1, await job.ProcessAsync(s.W.Tree.Org.Id, later, default));
        }

        using (var scope = new EngineScope(s.Host.Host.Services))
        {
            Assert.Equal(0, await scope.Services.GetRequiredService<Lms.Infrastructure.Grading.Publishing.ScheduledPublishJob>()
                .ProcessAsync(s.W.Tree.Org.Id, later, default)); // idempotent
        }

        Assert.Equal(GradeItem.PublishedState, Assert.Single(await ItemsAsync(s.A.Id)).PublishState);
        Assert.Single(await NoticesAsync(s.StudentUser)); // one notice, not one per run
    }

    [Fact]
    public async Task Scheduled_publish_runs_each_client_in_its_own_context()
    {
        var a = await SetupAsync("PB4A");
        var b = await SetupAsync("PB4B");
        using var _ha = a.Host;
        using var _hb = b.Host;
        var at = DateTime.UtcNow.AddMinutes(30);
        await PublishAsync(a, new(new[] { a.A.Id }, PublishModes.Schedule, at, false));
        await PublishAsync(b, new(new[] { b.A.Id }, PublishModes.Schedule, at, false));
        var later = DateTime.UtcNow.AddHours(1);

        using (var scope = new EngineScope(a.Host.Host.Services))
        {
            Assert.Equal(1, await scope.Services.GetRequiredService<Lms.Infrastructure.Grading.Publishing.ScheduledPublishJob>()
                .ProcessAsync(a.W.Tree.Org.Id, later, default));
        }

        Assert.Equal(GradeItem.PublishedState, Assert.Single(await ItemsAsync(a.A.Id)).PublishState);
        Assert.Equal(GradeItem.ScheduledState, Assert.Single(await ItemsAsync(b.A.Id)).PublishState); // client B's item is untouched by client A's run
        Assert.Empty(await NoticesAsync(b.StudentUser));
    }

    [Fact]
    public async Task Publish_under_a_provisional_scale_is_409_without_acknowledgement_and_the_acknowledgement_is_audited()
    {
        var s = await SetupAsync("PB5");
        using var _h = s.Host;
        var presets = await s.Host.SendAsync(s.W.Admin, Post, Routes.OrgUnitGradingScalePresets(s.W.Tree.School1.Id), new CopyScalePresetRequest("sa-school-5band"));
        var scale = (await presets.Content.ReadFromJsonAsync<GradingScaleDto>())!;
        Assert.True(scale.Provisional);
        var config = await s.Host.SendAsync(s.W.Admin, HttpMethod.Put, s.Host.Url(s.W, "/config", s.W.Gb.First.Id),
            new SaveGradebookConfigRequest(scale.Id, 2, CategoryMethods.PointsWeighted, MissingPolicies.Normalise));
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);

        var refused = await PublishAsync(s, Now(s.A.Id));
        Assert.Equal((HttpStatusCode.Conflict, "scale_provisional"), (refused.StatusCode, await CodeAsync(refused)));
        Assert.Equal(GradeItem.HiddenState, Assert.Single(await ItemsAsync(s.A.Id)).PublishState);
        Assert.Equal(HttpStatusCode.OK, (await PublishAsync(s, new(new[] { s.A.Id }, PublishModes.Hide, null, false))).StatusCode); // hiding never needs it

        var acknowledged = await PublishAsync(s, new(new[] { s.A.Id }, PublishModes.Now, null, true));
        Assert.Equal(HttpStatusCode.OK, acknowledged.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.Set<AuditEntry>().Where(a => a.OrganizationId == s.W.Tree.Org.Id).ToListAsync();
        Assert.Contains(entries, a => (a.EntityType + a.BeforeJson + a.AfterJson).Contains("PublishedUnderProvisionalScale", StringComparison.Ordinal));

        // after the admin confirms the scale no acknowledgement is needed
        await s.Host.SendAsync(s.W.Admin, Post, Routes.GradingScaleConfirm(s.W.Tree.School1.Id, scale.Id));
        Assert.Equal(HttpStatusCode.OK, (await PublishAsync(s, Now(s.B.Id))).StatusCode);
    }

    [Fact]
    public async Task Unpublish_hides_again_and_is_audited()
    {
        var s = await SetupAsync("PB6");
        using var _h = s.Host;
        await PublishAsync(s, Now(s.A.Id));

        var response = await PublishAsync(s, new(new[] { s.A.Id }, PublishModes.Hide, null, false));

        var result = (await response.Content.ReadFromJsonAsync<PublishResultDto>())!;
        Assert.Equal((1, 0, 0), (result.ItemsChanged, result.AffectedStudents, result.NotificationsQueued));
        var item = Assert.Single(await ItemsAsync(s.A.Id));
        Assert.Equal((GradeItem.HiddenState, null), (item.PublishState, item.PublishAtUtc));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var audits = await db.Set<AuditEntry>().Where(a => a.EntityType == nameof(GradeItem) && a.EntityId == s.A.Id).ToListAsync();
        Assert.Contains(audits, a => a.AfterJson is not null && a.AfterJson.Contains("Hidden", StringComparison.Ordinal)); // the state change is on the audit trail
    }

    [Fact]
    public async Task One_notification_per_student_per_batch_for_the_student_and_the_guardian()
    {
        var s = await SetupAsync("PB7");
        using var _h = s.Host;

        await PublishAsync(s, Now(s.A.Id, s.B.Id)); // two items, one batch

        Assert.Single(await NoticesAsync(s.StudentUser));
        Assert.Single(await NoticesAsync(s.GuardianUser));
        await PublishAsync(s, Now(s.A.Id, s.B.Id)); // nothing new
        Assert.Single(await NoticesAsync(s.StudentUser));
    }

    [Fact]
    public async Task Notification_contains_no_grade_value_or_item_title()
    {
        var s = await SetupAsync("PB8");
        using var _h = s.Host;

        await PublishAsync(s, Now(s.A.Id, s.B.Id));

        var notice = Assert.Single(await NoticesAsync(s.StudentUser));
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(notice.ParametersJson)!;
        Assert.Equal(["CourseNameAr", "CourseNameEn"], parameters.Keys.Order());
        var text = notice.ParametersJson;
        Assert.DoesNotContain("Quiz A", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Quiz B", text, StringComparison.Ordinal);
        Assert.DoesNotContain("15", text, StringComparison.Ordinal);
        Assert.Null(notice.DeepLink);
    }

    [Fact]
    public async Task Publish_writes_snapshots_per_affected_student_that_keep_values_out_of_the_audit_log()
    {
        var s = await SetupAsync("PB9");
        using var _h = s.Host;

        await PublishAsync(s, Now(s.A.Id, s.B.Id));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var snapshots = await db.Set<GradeSnapshot>().Where(x => x.OrganizationId == s.W.Tree.Org.Id).ToListAsync();
        Assert.Equal(2, snapshots.Count); // students 0 and 1 have scores; student 2 has none
        Assert.All(snapshots, x => Assert.Equal(GradeSnapshot.PublishKind, x.Kind));
        Assert.Single(snapshots.Select(x => x.BatchId).Distinct());
        var first = snapshots.Single(x => x.StudentProfileId == s.W.Students[0].Id);
        Assert.Equal(2, first.ItemCount);
        Assert.Equal(1, snapshots.Single(x => x.StudentProfileId == s.W.Students[1].Id).ItemCount);
        using var json = JsonDocument.Parse(first.ValuesJson);
        Assert.Equal([8m, 15m], json.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("points").GetDecimal()).Order());

        // the values live in the snapshot only: the audit entry of the row has no ValuesJson
        var audit = await db.Set<AuditEntry>().Where(a => a.EntityType == nameof(GradeSnapshot) && a.EntityId == first.Id).ToListAsync();
        Assert.NotEmpty(audit);
        Assert.All(audit, a => Assert.DoesNotContain("points", (a.AfterJson ?? string.Empty) + (a.BeforeJson ?? string.Empty), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Publishing_follows_the_permission_and_the_relationship_and_is_allowed_on_a_locked_gradebook()
    {
        var s = await SetupAsync("PB10");
        using var _h = s.Host;

        Assert.Equal(HttpStatusCode.OK, (await PublishAsync(s, Now(s.A.Id), s.W.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PublishAsync(s, Now(s.B.Id), s.W.Supervisor)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublishAsync(s, Now(s.B.Id), s.W.OtherTeacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PublishAsync(s, Now(Guid.NewGuid()))).StatusCode);

        var grid = await s.Host.GridAsync(s.W);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Gradebook>().SingleAsync(g => g.Id == grid.Id)).Lock(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        // publishing is visibility, not a grade write: a locked gradebook can still be published (lock, then release is the year-end flow)
        Assert.Equal(HttpStatusCode.OK, (await PublishAsync(s, Now(s.B.Id))).StatusCode);
        Assert.Equal(GradeItem.PublishedState, Assert.Single(await ItemsAsync(s.B.Id)).PublishState);
    }

    [Fact]
    public async Task An_item_of_another_class_course_is_a_miss_for_publishing()
    {
        var a = await SetupAsync("PB11A");
        var b = await SetupAsync("PB11B");
        using var _ha = a.Host;
        using var _hb = b.Host;

        Assert.Equal(HttpStatusCode.NotFound, (await PublishAsync(a, Now(b.A.Id))).StatusCode);
        Assert.Equal(GradeItem.HiddenState, Assert.Single(await ItemsAsync(b.A.Id)).PublishState);
    }
}
