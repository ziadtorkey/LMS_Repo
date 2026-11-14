using Lms.Domain.Grading;
using Lms.Domain.Grading.Calculation;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Reads;

/// <summary>
/// Loads the rows of one gradebook for some students and runs the engine on them (M5 Task 7 records mapped onto the #631 inputs). With
/// <c>publishedOnly</c> the unpublished items are never loaded, so nothing computed or returned afterwards can carry a hidden item (L6).
/// Read only: it never creates rows.
/// </summary>
public sealed class GradebookCalculation(LmsDbContext db, BandLegendReader legends)
{
    public sealed record Loaded(
        Gradebook Book, IReadOnlyList<GradeCategory> Categories, IReadOnlyList<GradeItem> Items, VisibleScale? Scale,
        IReadOnlyList<CalcBand> Bands, CalculationResult Result);

    public async Task<Loaded> RunAsync(Gradebook book, IReadOnlyList<Guid> students, bool publishedOnly, CancellationToken ct)
    {
        var itemQuery = db.Set<GradeItem>().Where(i => i.GradebookId == book.Id);
        if (publishedOnly)
        {
            itemQuery = itemQuery.Where(i => i.PublishState == GradeItem.PublishedState);
        }

        var items = await itemQuery.OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync(ct);
        var categoryIds = items.Select(i => i.CategoryId).Distinct().ToList();
        var categoryQuery = db.Set<GradeCategory>().Where(c => c.GradebookId == book.Id);
        if (publishedOnly)
        {
            categoryQuery = categoryQuery.Where(c => categoryIds.Contains(c.Id));
        }

        var categories = await categoryQuery.OrderBy(c => c.Order).ThenBy(c => c.Id).ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var entries = await db.Set<GradeEntry>()
            .Where(e => e.GradebookId == book.Id && itemIds.Contains(e.GradeItemId) && students.Contains(e.StudentProfileId)).ToListAsync(ct);
        var events = await db.Set<GradeEvent>()
            .Where(e => e.ClassCourseId == book.ClassCourseId && students.Contains(e.StudentProfileId)).ToListAsync(ct);

        var byItem = items.Where(i => i.SourceId is not null)
            .GroupBy(i => (Kind: i.SourceKind, Source: i.SourceId)).ToDictionary(g => g.Key, g => g.First().Id);
        var calcEvents = new List<CalcEvent>();
        foreach (var e in events)
        {
            if (byItem.TryGetValue(((int?)(int)e.SourceKind, (Guid?)e.SourceId), out var itemId))
            {
                calcEvents.Add(new CalcEvent(itemId, e.StudentProfileId, e.Revision, e.Points));
            }
        }

        var scale = await legends.ScaleOfAsync(book, ct);
        var bands = scale is null
            ? new List<CalcBand>()
            : scale.Bands.OrderBy(b => b.MinPercent)
                .Select(b => new CalcBand(b.MinPercent, b.LabelAr ?? b.Code, b.LabelEn ?? b.Code, b.Points)).ToList();

        var input = new CalculationInput(
            categories.Select(c => new CalcCategory(c.Id, c.Weight)).ToList(),
            items.Select(i => new CalcItem(i.Id, i.CategoryId, i.MaxPoints, i.PublishState == GradeItem.PublishedState, i.SourceKind)).ToList(),
            calcEvents,
            entries.Select(e => new CalcEntry(e.GradeItemId, e.StudentProfileId, e.Points, e.IsOverride, e.IsExcused)).ToList(),
            bands,
            new CalcRules(
                Enum.TryParse<MissingPolicy>(book.MissingPolicy, true, out var missing) ? missing : MissingPolicy.Normalise,
                Enum.TryParse<CategoryMethod>(book.CategoryMethod, true, out var method) ? method : CategoryMethod.PointsWeighted,
                book.DecimalPlaces,
                publishedOnly),
            students);

        return new Loaded(book, categories, items, scale, bands, GradebookCalculator.Calculate(input));
    }

    /// <summary>The band code of a band the engine returned (the engine carries labels and points only).</summary>
    public static string? CodeOf(Loaded loaded, CalcBand? band) =>
        band is null ? null : loaded.Scale?.Bands.FirstOrDefault(b => b.MinPercent == band.MinPercent)?.Code;
}
