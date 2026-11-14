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

namespace Lms.Infrastructure.Grading.Publishing;

/// <summary>
/// The result of a publish call. <c>AffectedStudents</c> are the students whose visible grades changed (they get a snapshot); <c>NotificationsQueued</c>
/// counts the students who were told (one notice per student per batch, with their linked guardians as recipients, L26).
/// </summary>
public sealed record PublishOutcome(int ItemsChanged, int AffectedStudents, int NotificationsQueued);

/// <summary>
/// Publishing of grade items (M5 Task 11, FR-GRD-05, L6, L7, L26): now, on a schedule (the server clock) or hidden again. A publish under a provisional scale
/// is refused unless acknowledged (the acknowledgement is audited); un-publishing is audited through the item. Every publish-now writes one
/// <see cref="GradeSnapshot"/> per affected student and sends one <c>Grades.Published</c> notification per student: course name only, no value, no title.
/// </summary>
public sealed class GradePublisher(
    LmsDbContext db, GradebookReader reader, IScaleResolver scales, GradePublishedNotifier notifier,
    IAuditWriter audit, IClock clock)
{
    public const int MaxItems = 500;

    public async Task<PublishOutcome> PublishAsync(ClassCourse classCourse, PublishItemsRequest request, Guid? byUserId, CancellationToken ct)
    {
        if (request is null || !PublishModes.All.Contains(request.Mode) || request.ItemIds is null || request.ItemIds.Count == 0
            || request.ItemIds.Count > MaxItems)
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        var ids = request.ItemIds.Distinct().ToList();
        var items = await (from i in db.Set<GradeItem>()
                           join g in db.Set<Gradebook>() on i.GradebookId equals g.Id
                           where ids.Contains(i.Id) && g.ClassCourseId == classCourse.Id
                           select i).ToListAsync(ct);
        if (items.Count != ids.Count)
        {
            throw new GradebookRuleException(ProblemCodes.not_found, 404); // an item of another class course is a miss
        }

        var gradebookId = items.Select(i => i.GradebookId).Distinct().ToList();
        if (gradebookId.Count != 1)
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        // Publishing is visibility, not a grade write: a locked gradebook or a closed class or year can still be published (lock, then release is the year-end flow).
        var gradebook = await db.Set<Gradebook>().SingleAsync(g => g.Id == gradebookId[0], ct);

        var now = clock.UtcNow;
        if (request.Mode == PublishModes.Schedule && (request.AtUtc is not { } at || at <= now))
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400); // a schedule must lie after the server clock
        }

        if (request.Mode != PublishModes.Hide)
        {
            await EnsureScaleAcknowledgedAsync(classCourse, gradebook, request, items, byUserId, ct);
        }

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

        await db.SaveChangesAsync(ct);
        return request.Mode == PublishModes.Now
            ? await AnnounceAsync(classCourse, gradebook, changed, byUserId, ct)
            : new PublishOutcome(changed.Count, 0, 0);
    }

    /// <summary>Publishes items whose schedule is due (the scheduled job): same snapshots and notices as a publish-now, no acting user.</summary>
    public async Task<PublishOutcome> PublishDueAsync(Gradebook gradebook, IReadOnlyList<GradeItem> due, CancellationToken ct)
    {
        var now = clock.UtcNow;
        foreach (var item in due)
        {
            item.Publish(now);
        }

        await db.SaveChangesAsync(ct);
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == gradebook.ClassCourseId, ct);
        return await AnnounceAsync(classCourse, gradebook, due, null, ct);
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

    /// <summary>Snapshots and notices for the students whose visible grades the newly published items changed.</summary>
    private async Task<PublishOutcome> AnnounceAsync(
        ClassCourse classCourse, Gradebook gradebook, IReadOnlyList<GradeItem> changed, Guid? byUserId, CancellationToken ct)
    {
        if (changed.Count == 0)
        {
            return new PublishOutcome(0, 0, 0);
        }

        var data = await reader.LoadActiveAsync(classCourse, gradebook, ct);
        var result = data.Calculate(publishedOnly: true);
        var changedIds = changed.Select(i => i.Id).ToHashSet();
        var maxPoints = changed.ToDictionary(i => i.Id, i => i.MaxPoints);
        var batch = Guid.NewGuid();
        var now = clock.UtcNow;
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

        await db.SaveChangesAsync(ct);
        var notified = await notifier.NotifyAsync(classCourse, affected, ct);
        return new PublishOutcome(changed.Count, affected.Count, notified);
    }
}
