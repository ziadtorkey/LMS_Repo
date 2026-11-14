using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Domain.Grading.Calculation;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Grading.Publishing;

/// <summary>
/// The result of a publish call. <c>AffectedStudents</c> are the students whose visible grades changed (they get a snapshot); <c>NotificationsQueued</c>
/// counts the students who were told (one notice per student per batch, with their linked guardians as recipients, L26).
/// </summary>
public sealed record PublishOutcome(int ItemsChanged, int AffectedStudents, int NotificationsQueued);

/// <summary>
/// Publishing of grade items (M5 Task 11, FR-GRD-05, L6, L7, L26): now, on a schedule (the server clock) or hidden again. A publish under a provisional scale
/// is refused unless acknowledged (the acknowledgement is audited); un-publishing is audited through the item and with the acting user. Every publish-now
/// writes one <see cref="GradeSnapshot"/> per affected student <b>in the same commit as the item change</b> and then sends one <c>Grades.Published</c>
/// notification per student: course name only, no value, no title. A failed notice is logged and never undoes the publish.
/// Publishing is visibility, not a grade write: it is allowed on a locked gradebook and in a closed class or year (the scheduled job too).
/// Only students with an active enrollment get a snapshot and a notice: a student who left the course is not told (and neither is their guardian).
/// <c>PublishState</c> is a concurrency token, so when two runners publish the same item only one commits and announces it.
/// </summary>
public sealed class GradePublisher(
    LmsDbContext db, GradebookReader reader, IScaleResolver scales, GradePublishedNotifier notifier,
    IAuditWriter audit, IClock clock, ILogger<GradePublisher> logger)
{
    public const int MaxItems = 500;
    private const int Attempts = 3;

    public async Task<PublishOutcome> PublishAsync(ClassCourse classCourse, PublishItemsRequest request, Guid? byUserId, CancellationToken ct)
    {
        if (request is null || !PublishModes.All.Contains(request.Mode) || request.ItemIds is null || request.ItemIds.Count == 0
            || request.ItemIds.Count > MaxItems)
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        var ids = request.ItemIds.Distinct().ToList();
        for (var attempt = 1; ; attempt++)
        {
            var items = await (from i in db.Set<GradeItem>()
                               join g in db.Set<Gradebook>() on i.GradebookId equals g.Id
                               where ids.Contains(i.Id) && g.ClassCourseId == classCourse.Id
                               select i).ToListAsync(ct);
            if (items.Count != ids.Count)
            {
                throw new GradebookRuleException(ProblemCodes.not_found, 404); // an item of another class course is a miss
            }

            var gradebookIds = items.Select(i => i.GradebookId).Distinct().ToList();
            if (gradebookIds.Count != 1)
            {
                throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
            }

            var now = clock.UtcNow;
            if (request.Mode == PublishModes.Schedule && (request.AtUtc is not { } at || at <= now))
            {
                throw new GradebookRuleException(ProblemCodes.validation_failed, 400); // a schedule must lie after the server clock
            }

            var gradebook = await db.Set<Gradebook>().SingleAsync(g => g.Id == gradebookIds[0], ct);
            if (request.Mode != PublishModes.Hide)
            {
                await EnsureScaleAcknowledgedAsync(classCourse, gradebook, request, items, byUserId, ct);
            }

            var changed = ApplyMode(items, request, now);
            var affected = request.Mode == PublishModes.Now && changed.Count > 0
                ? await SnapshotAsync(classCourse, gradebook, changed, byUserId, now, ct)
                : [];
            try
            {
                await db.SaveChangesAsync(ct); // the item changes and their snapshots are one commit
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
                db.ChangeTracker.Clear(); // another runner published or changed the same items first: look again, announce only what is still ours
                continue;
            }

            if (request.Mode == PublishModes.Hide && changed.Count > 0)
            {
                await audit.WriteSecurityEventAsync(
                    "Grades.Hidden", byUserId, $"gradebook={gradebook.Id}; items={string.Join(',', changed.Select(i => i.Id))}", cancellationToken: ct);
            }

            var told = await NotifyAsync(classCourse, affected, ct);
            return new PublishOutcome(changed.Count, affected.Count, told);
        }
    }

    /// <summary>Publishes items whose schedule is due (the scheduled job): same snapshots and notices as a publish-now, no acting user.</summary>
    public async Task<PublishOutcome> PublishDueAsync(Gradebook gradebook, IReadOnlyList<Guid> dueItemIds, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var due = await db.Set<GradeItem>()
                .Where(i => dueItemIds.Contains(i.Id) && i.PublishState == GradeItem.ScheduledState).ToListAsync(ct);
            if (due.Count == 0)
            {
                return new PublishOutcome(0, 0, 0); // another runner got there first
            }

            var now = clock.UtcNow;
            foreach (var item in due)
            {
                item.Publish(now);
            }

            var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == gradebook.ClassCourseId, ct);
            var affected = await SnapshotAsync(classCourse, gradebook, due, null, now, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            return new PublishOutcome(due.Count, affected.Count, await NotifyAsync(classCourse, affected, ct));
        }
    }

    private static List<GradeItem> ApplyMode(IReadOnlyList<GradeItem> items, PublishItemsRequest request, DateTime now)
    {
        var changed = new List<GradeItem>();
        foreach (var item in items)
        {
            switch (request.Mode)
            {
                case PublishModes.Now when item.PublishState != GradeItem.PublishedState:
                    item.Publish(now);
                    changed.Add(item);
                    break;
                case PublishModes.Schedule when item.PublishState != GradeItem.ScheduledState || item.PublishAtUtc != request.AtUtc:
                    item.Schedule(request.AtUtc!.Value);
                    changed.Add(item);
                    break;
                case PublishModes.Hide when item.PublishState != GradeItem.HiddenState:
                    item.Hide();
                    changed.Add(item);
                    break;
            }
        }

        return changed;
    }

    private async Task EnsureScaleAcknowledgedAsync(
        ClassCourse classCourse, Gradebook gradebook, PublishItemsRequest request, IReadOnlyList<GradeItem> items, Guid? byUserId, CancellationToken ct)
    {
        if (gradebook.GradingScaleId is not { } scaleId)
        {
            return;
        }

        var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == classCourse.OrgUnitId, ct);
        var scale = unit is null ? null : await scales.FindScaleAsync(unit, scaleId, ct);
        if (scale?.Scale.Provisional != true)
        {
            return;
        }

        if (!request.AcknowledgeProvisionalScale)
        {
            throw new GradebookRuleException(ProblemCodes.scale_provisional);
        }

        // L7: publishing under an unconfirmed scale is a decision someone took; ids only, no values.
        await audit.WriteSecurityEventAsync(
            "Grades.PublishedUnderProvisionalScale", byUserId,
            $"gradebook={gradebook.Id}; scale={scaleId}; items={string.Join(',', items.Select(i => i.Id))}", cancellationToken: ct);
    }

    /// <summary>
    /// Adds one snapshot per active student whose visible grades the newly published items changed (to the unit of work, not saved) and returns their ids.
    /// The items are already marked published in memory, so the engine's published-only run sees them; the load returns those tracked instances.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> SnapshotAsync(
        ClassCourse classCourse, Gradebook gradebook, IReadOnlyList<GradeItem> changed, Guid? byUserId, DateTime now, CancellationToken ct)
    {
        var data = await reader.LoadActiveAsync(classCourse, gradebook, ct);
        var result = data.Calculate(publishedOnly: true);
        var changedIds = changed.Select(i => i.Id).ToHashSet();
        var maxPoints = changed.ToDictionary(i => i.Id, i => i.MaxPoints);
        var batch = Guid.NewGuid();
        var affected = new List<Guid>();
        foreach (var student in result.Students)
        {
            var shown = student.Items.Where(r => changedIds.Contains(r.ItemId) && r.Source != ItemSource.Missing).ToList();
            if (shown.Count == 0)
            {
                continue;
            }

            var values = JsonSerializer.Serialize(new
            {
                items = shown.Select(r => new { itemId = r.ItemId, source = r.Source.ToString(), points = r.Points, max = maxPoints[r.ItemId] }),
                finalPercent = student.FinalPercent,
                band = student.Band?.LabelEn,
            });
            db.Add(new GradeSnapshot(gradebook, student.StudentId, GradeSnapshot.PublishKind, batch, shown.Count, values, byUserId, now));
            affected.Add(student.StudentId);
        }

        return affected;
    }

    /// <summary>The notices go after the commit: a failure is logged and never undoes the publish.</summary>
    private async Task<int> NotifyAsync(ClassCourse classCourse, IReadOnlyList<Guid> studentIds, CancellationToken ct)
    {
        try
        {
            return await notifier.NotifyAsync(classCourse, studentIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The grades-published notices of class course {ClassCourseId} failed; the items stay published", classCourse.Id);
            return 0;
        }
    }
}
