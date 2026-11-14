using Lms.Domain.Grading;
using Lms.Domain.Grading.Calculation;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>A student of the grid. <c>WithdrawnOn</c> is set when the enrollment ended (the student left mid-term).</summary>
public sealed record GridStudent(Guid Id, string Code, string NameAr, string NameEn, bool IsWithdrawn = false, DateOnly? WithdrawnOn = null);

/// <summary>A band of the gradebook's scale with its code (the calculator's <see cref="CalcBand"/> has none).</summary>
public sealed record GridBand(string Code, CalcBand Band);

/// <summary>
/// Everything the staff grid needs for a set of students, loaded once: the gradebook's categories and items, the event scores and the teacher entries of
/// those students, the bands of its scale. The calculation runs over it with <see cref="GradebookCalculator"/>; the server computes, the client never does (L18).
/// </summary>
public sealed class GradebookData
{
    public required Gradebook Gradebook { get; init; }
    public required IReadOnlyList<GradeCategory> Categories { get; init; }
    public required IReadOnlyList<GradeItem> Items { get; init; }
    public required IReadOnlyList<GradeEvent> Events { get; init; }
    public required IReadOnlyList<GradeEntry> Entries { get; init; }
    public required IReadOnlyList<GridBand> Bands { get; init; }
    public required IReadOnlyList<GridStudent> Students { get; init; }
    public required IReadOnlyList<Guid> StudentIds { get; init; }

    /// <summary>The gradebook's scale as its unit sees it; null until one is chosen.</summary>
    public VisibleScale? Scale { get; init; }

    /// <summary>The event items by (source kind, source id) so an event finds its column.</summary>
    public IReadOnlyDictionary<(int Kind, Guid Source), GradeItem> BySource =>
        Items.Where(i => i.SourceKind is not null && i.SourceId is not null).ToDictionary(i => (i.SourceKind!.Value, i.SourceId!.Value));

    public CalculationResult Calculate()
    {
        var bySource = BySource;
        var rules = new CalcRules(
            Enum.TryParse<MissingPolicy>(Gradebook.MissingPolicy, out var missing) ? missing : MissingPolicy.Normalise,
            Enum.TryParse<CategoryMethod>(Gradebook.CategoryMethod, out var method) ? method : CategoryMethod.PointsWeighted,
            Gradebook.DecimalPlaces);
        var events = Events
            .Where(e => bySource.ContainsKey(((int)e.SourceKind, e.SourceId)))
            .Select(e => new CalcEvent(bySource[((int)e.SourceKind, e.SourceId)].Id, e.StudentProfileId, e.Revision, e.Points))
            .ToList();
        return GradebookCalculator.Calculate(new CalculationInput(
            Categories.Select(c => new CalcCategory(c.Id, c.Weight)).ToList(),
            Items.Select(i => new CalcItem(i.Id, i.CategoryId, i.MaxPoints, i.PublishState == GradeItem.PublishedState, i.SourceKind)).ToList(),
            events,
            Entries.Select(e => new CalcEntry(e.GradeItemId, e.StudentProfileId, e.Points, e.IsOverride, e.IsExcused)).ToList(),
            Bands.Select(b => b.Band).ToList(),
            rules,
            StudentIds));
    }
}
