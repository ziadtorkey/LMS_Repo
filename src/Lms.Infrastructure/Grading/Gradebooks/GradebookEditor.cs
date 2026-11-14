using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

public sealed record CategorySpec(Guid? Id, string NameAr, string NameEn, decimal Weight, int Order);

/// <summary>
/// The staff edits of a gradebook (M5 Task 8): configuration, categories, manual items and the entries of a student's row. Every write first goes
/// through <see cref="IGradebookWriter.EnsureWritableAsync"/> (locked gradebook, closed class or year) and refuses with a
/// <see cref="GradebookRuleException"/>; the caller commits nothing else, this class saves.
/// </summary>
public sealed class GradebookEditor(LmsDbContext db, IGradebookWriter writer, IScaleResolver scales, IClock clock)
{
    public async Task ConfigureAsync(ClassCourse classCourse, Gradebook gradebook, SaveGradebookConfigRequest request, CancellationToken ct)
    {
        await writer.EnsureWritableAsync(gradebook, ct);
        if (request.DecimalPlaces is < GradingLimits.DecimalPlacesMin or > GradingLimits.DecimalPlacesMax
            || !CategoryMethods.All.Contains(request.CategoryMethod) || !MissingPolicies.All.Contains(request.MissingPolicy))
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        if (request.ScaleId is { } scaleId)
        {
            var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == classCourse.OrgUnitId, ct);
            var visible = unit is null ? null : await scales.FindScaleAsync(unit, scaleId, ct);
            if (visible is null)
            {
                throw new GradebookRuleException(ProblemCodes.not_found, 404); // a scale outside what the unit sees never resolves
            }
        }

        gradebook.Configure(request.ScaleId, request.DecimalPlaces, request.CategoryMethod, request.MissingPolicy);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Replaces the category list as a whole: listed ids are updated, null ids created, an omitted empty category deleted (one with items stays: 400).</summary>
    public async Task SaveCategoriesAsync(Gradebook gradebook, IReadOnlyList<CategorySpec> specs, CancellationToken ct)
    {
        await writer.EnsureWritableAsync(gradebook, ct);
        if (specs.Any(s => s.Weight is < GradingLimits.WeightMin or > GradingLimits.WeightMax))
        {
            throw new GradebookRuleException(ProblemCodes.weights_invalid, 400);
        }

        var stored = await db.Set<GradeCategory>().Where(c => c.GradebookId == gradebook.Id).ToListAsync(ct);
        var byId = stored.ToDictionary(c => c.Id);
        if (specs.Any(s => s.Id is { } id && !byId.ContainsKey(id)) || specs.Where(s => s.Id is not null).GroupBy(s => s.Id).Any(g => g.Count() > 1))
        {
            throw new GradebookRuleException(ProblemCodes.not_found, 404);
        }

        var listed = specs.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
        var withItems = await db.Set<GradeItem>().Where(i => i.GradebookId == gradebook.Id).Select(i => i.CategoryId).Distinct().ToListAsync(ct);
        foreach (var gone in stored.Where(c => !listed.Contains(c.Id)))
        {
            if (gone.IsUncategorised || withItems.Contains(gone.Id))
            {
                throw new GradebookRuleException(ProblemCodes.validation_failed, 400); // the built-in category and categories with items stay
            }

            db.Remove(gone);
        }

        foreach (var spec in specs)
        {
            if (spec.Id is { } id)
            {
                var category = byId[id];
                category.Update(spec.NameAr, spec.NameEn, category.IsUncategorised ? category.Weight : spec.Weight, spec.Order);
            }
            else
            {
                db.Add(new GradeCategory(gradebook, spec.NameAr, spec.NameEn, spec.Weight, spec.Order));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<GradeItem> CreateItemAsync(Gradebook gradebook, CreateGradeItemRequest request, CancellationToken ct)
    {
        var item = await writer.AddManualItemAsync(gradebook, request.CategoryId, request.TitleAr.Trim(), request.TitleEn.Trim(), request.MaxPoints, ct);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>Moves an item and, for a manual one, renames it and changes its maximum (a maximum below a stored score is 422).</summary>
    public async Task UpdateItemAsync(Gradebook gradebook, GradeItem item, UpdateGradeItemRequest request, CancellationToken ct)
    {
        await writer.EnsureWritableAsync(gradebook, ct);
        var category = await db.Set<GradeCategory>().FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.GradebookId == gradebook.Id, ct)
                       ?? throw new GradebookRuleException(ProblemCodes.not_found, 404);
        if (item.IsManual)
        {
            if (request.MaxPoints < item.MaxPoints
                && await db.Set<GradeEntry>().AnyAsync(e => e.GradeItemId == item.Id && e.Points > request.MaxPoints, ct))
            {
                throw new GradebookRuleException(ProblemCodes.grade_out_of_range, 422);
            }

            item.Rename(request.TitleAr.Trim(), request.TitleEn.Trim());
            item.SetMaxPoints(request.MaxPoints);
        }

        item.MoveTo(category, request.Order);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a manual item with its entries. An item of a grade event source stays (400).</summary>
    public async Task DeleteItemAsync(Gradebook gradebook, GradeItem item, CancellationToken ct)
    {
        await writer.EnsureWritableAsync(gradebook, ct);
        if (!item.IsManual)
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        db.RemoveRange(await db.Set<GradeEntry>().Where(e => e.GradeItemId == item.Id).ToListAsync(ct));
        db.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The changes of one student, all or none. The row <paramref name="version"/> must be the one the page loaded (409 <c>concurrency_conflict</c>);
    /// points are 0 to the item's maximum (422), an override needs a reason (422); event scores are never written here: a manual score is
    /// <c>Set</c> on a manual item, an event score is replaced by <c>Override</c> (L12).
    /// </summary>
    public async Task ApplyEntriesAsync(
        Gradebook gradebook, Guid studentId, long version, IReadOnlyList<GradeEntryChange> changes, Guid? byUserId, CancellationToken ct)
    {
        await writer.EnsureWritableAsync(gradebook, ct);
        var enrolled = await db.Set<CourseEnrollment>().AnyAsync(
            e => e.ClassCourseId == gradebook.ClassCourseId && e.StudentId == studentId && e.Status == CourseEnrollment.ActiveStatus, ct);
        if (!enrolled)
        {
            throw new GradebookRuleException(ProblemCodes.not_found, 404);
        }

        var items = (await db.Set<GradeItem>().Where(i => i.GradebookId == gradebook.Id).ToListAsync(ct)).ToDictionary(i => i.Id);
        var entries = (await db.Set<GradeEntry>().Where(e => e.GradebookId == gradebook.Id && e.StudentProfileId == studentId).ToListAsync(ct))
            .ToDictionary(e => e.GradeItemId);
        if (GradeRowBuilder.RowVersion(entries.Values) != version)
        {
            throw new GradebookRuleException(ProblemCodes.concurrency_conflict);
        }

        foreach (var change in changes)
        {
            Validate(change, items.GetValueOrDefault(change.ItemId), entries.GetValueOrDefault(change.ItemId));
        }

        var now = clock.UtcNow;
        foreach (var change in changes)
        {
            var item = items[change.ItemId];
            if (!entries.TryGetValue(item.Id, out var entry))
            {
                entry = new GradeEntry(item, studentId, byUserId, now);
                db.Add(entry);
                entries[item.Id] = entry;
            }

            switch (change.Action)
            {
                case GradeEntryActions.Set: entry.SetPoints(change.Points, byUserId, now); break;
                case GradeEntryActions.Override: entry.SetOverride(change.Points!.Value, change.Reason!.Trim(), byUserId, now); break;
                case GradeEntryActions.ClearOverride:
                case GradeEntryActions.DropOverride: entry.ClearOverride(byUserId, now); break;
                case GradeEntryActions.Excuse: entry.Excuse(byUserId, now); break;
                case GradeEntryActions.KeepOverride: entry.Acknowledge(byUserId, now); break;
                default: ClearCell(entry, byUserId, now); break;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static void ClearCell(GradeEntry entry, Guid? byUserId, DateTime now)
    {
        if (entry.IsExcused)
        {
            entry.ClearExcuse(byUserId, now);
        }
        else if (entry.IsOverride)
        {
            entry.ClearOverride(byUserId, now);
        }
        else
        {
            entry.SetPoints(null, byUserId, now);
        }
    }

    private static void Validate(GradeEntryChange change, GradeItem? item, GradeEntry? entry)
    {
        if (item is null)
        {
            throw new GradebookRuleException(ProblemCodes.not_found, 404);
        }

        if (!GradeEntryActions.All.Contains(change.Action))
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        switch (change.Action)
        {
            case GradeEntryActions.Set when !item.IsManual: // event scores are read only; replace one with Override
                throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
            case GradeEntryActions.Override when item.IsManual:
                throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
            case GradeEntryActions.KeepOverride or GradeEntryActions.DropOverride when entry is not { IsOverride: true }:
                throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        if (change.Action == GradeEntryActions.Override
            && (string.IsNullOrWhiteSpace(change.Reason) || change.Reason.Trim().Length > GradingLimits.ReasonMaxLength))
        {
            throw new GradebookRuleException(ProblemCodes.grade_override_reason_required, 422);
        }

        if (change.Action is GradeEntryActions.Set or GradeEntryActions.Override)
        {
            // Set with no points clears the cell; an override always carries points.
            if (change.Points is null && change.Action == GradeEntryActions.Override)
            {
                throw new GradebookRuleException(ProblemCodes.grade_out_of_range, 422);
            }

            if (change.Points is { } p && (p < 0 || p > item.MaxPoints))
            {
                throw new GradebookRuleException(ProblemCodes.grade_out_of_range, 422);
            }
        }
    }
}
