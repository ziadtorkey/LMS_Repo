using Lms.Api.Infrastructure;
using Lms.Domain.Assignments;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Assignments;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Lms.Shared.Notifications;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Assignments.AssignmentTestKit;

namespace Lms.IntegrationTests.Assignments;

/// <summary>M4a Task 9 (#359): the hourly "due soon" reminder.</summary>
[Collection(ApiCollection.Name)]
public sealed class DueSoonJobTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    /// <summary>Runs the job the way the scheduler does: in its own scope with the platform (system) context.</summary>
    private async Task RunAsync(DateTime now)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<AssignmentDueSoonJob>().RunAtAsync(now, default);
    }

    private async Task<List<Notification>> NoticesAsync(Guid user)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == user && n.EventKey == NotificationEvents.AssignmentsDueSoon).ToListAsync();
    }

    [Fact]
    public void Hourly_recurring_job_is_registered_at_startup()
    {
        using var scope = factory.Services.CreateScope();
        using var connection = scope.ServiceProvider.GetRequiredService<JobStorage>().GetConnection();

        var job = Assert.Single(connection.GetRecurringJobs(), r => r.Id == AssignmentDueSoonJob.RecurringJobId);

        Assert.Equal(AssignmentDueSoonJob.Cron, job.Cron);
    }

    [Fact]
    public async Task Due_soon_notifies_once_and_skips_submitted()
    {
        var w = await _t.SetUpAsync("DS1");
        var a = await CreateAsync(_t, w.Teacher, w, Request(due: DateTime.UtcNow.AddHours(20)));

        await RunAsync(DateTime.UtcNow);
        await RunAsync(DateTime.UtcNow.AddMinutes(30));

        var notice = Assert.Single(await NoticesAsync(w.Student.UserId));
        Assert.Contains(a.Id.ToString(), notice.DeepLink);

        // Submitted students are skipped (a second assignment, student already handed in).
        var b = await CreateAsync(_t, w.Teacher, w, Request("Done", due: DateTime.UtcNow.AddHours(10)));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await SubmitAsync(_t, w.Student, b.Id)).StatusCode);
        await RunAsync(DateTime.UtcNow);
        Assert.Single(await NoticesAsync(w.Student.UserId));
    }

    [Fact]
    public async Task Due_further_away_past_or_hidden_assignments_are_not_reminded()
    {
        var w = await _t.SetUpAsync("DS2");
        await CreateAsync(_t, w.Teacher, w, Request("Far", due: DateTime.UtcNow.AddDays(3)));
        await CreateAsync(_t, w.Teacher, w, Request("Past", due: DateTime.UtcNow.AddHours(-2)));
        await CreateAsync(_t, w.Teacher, w, Request("NoDue"));
        await CreateAsync(_t, w.Teacher, w, Request("Hidden", visibility: ModuleVisibilities.Hidden, due: DateTime.UtcNow.AddHours(5)));

        await RunAsync(DateTime.UtcNow);

        Assert.Empty(await NoticesAsync(w.Student.UserId));
    }

    [Fact]
    public async Task A_new_due_date_earns_a_new_reminder_and_group_targets_are_respected()
    {
        var w = await _t.SetUpAsync("DS3");
        var group = await _t.AddGroupAsync(w.ClassCourse); // the student is not in it
        await CreateAsync(_t, w.Teacher, w, Request("Grp", due: DateTime.UtcNow.AddHours(5), target: ModuleTargets.Group, groupId: group.Id));
        await RunAsync(DateTime.UtcNow);
        Assert.Empty(await NoticesAsync(w.Student.UserId));

        var a = await CreateAsync(_t, w.Teacher, w, Request("All", due: DateTime.UtcNow.AddHours(6)));
        await RunAsync(DateTime.UtcNow);
        Assert.Single(await NoticesAsync(w.Student.UserId));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var row = await db.Set<Assignment>().SingleAsync(x => x.Id == a.Id);
            Assert.NotNull(row.DueSoonNotifiedAtUtc);
        }
    }
}
