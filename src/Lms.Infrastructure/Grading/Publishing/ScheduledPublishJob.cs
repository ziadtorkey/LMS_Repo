using Lms.Domain.Abstractions;
using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Grading.Publishing;

/// <summary>
/// Publishes the grade items whose schedule is due (M5 Task 11, plan site 5). Runs every minute as a system job per client: each client is processed on its
/// own, so one client's failure never stops the others, and the query is limited to that client's rows. Idempotent: a published item is no longer
/// <c>Scheduled</c>, so a second run (or a second server) finds nothing; two runners at once cannot both commit the same item (<c>PublishState</c> is a
/// concurrency token), so a batch is announced once. A scheduled publish also runs on a locked gradebook and in a closed class or year: publishing is
/// visibility, not a grade write.
/// </summary>
public sealed class ScheduledPublishJob(LmsDbContext db, GradePublisher publisher, IClock clock, ILogger<ScheduledPublishJob> logger)
{
    public const string RecurringJobId = "grade-scheduled-publish";

    /// <summary>Every minute (the cron is read in <c>Jobs:TimeZone</c>).</summary>
    public const string Cron = "* * * * *";

    public async Task RunAsync(CancellationToken ct)
    {
        var organizationIds = await db.Set<Organization>().Select(o => o.Id).ToListAsync(ct);
        foreach (var organizationId in organizationIds)
        {
            try
            {
                await ProcessAsync(organizationId, clock.UtcNow, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Scheduled grade publishing failed for client {OrganizationId}", organizationId);
            }
        }
    }

    /// <summary>One client's due items, per gradebook. Returns how many items were published.</summary>
    public async Task<int> ProcessAsync(Guid organizationId, DateTime now, CancellationToken ct)
    {
        var due = await db.Set<GradeItem>()
            .Where(i => i.OrganizationId == organizationId && i.PublishState == GradeItem.ScheduledState && i.PublishAtUtc != null && i.PublishAtUtc <= now)
            .ToListAsync(ct);
        var published = 0;
        foreach (var group in due.GroupBy(i => i.GradebookId))
        {
            var gradebook = await db.Set<Gradebook>().SingleAsync(g => g.Id == group.Key, ct);
            published += (await publisher.PublishDueAsync(gradebook, group.Select(i => i.Id).ToList(), ct)).ItemsChanged;
        }

        return published;
    }
}
