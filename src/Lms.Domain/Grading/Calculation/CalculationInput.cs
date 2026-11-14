namespace Lms.Domain.Grading.Calculation;

/// <summary>What the engine does with an item that has no score for a student (setting <c>Grades.MissingPolicy</c>).</summary>
public enum MissingPolicy
{
    /// <summary>The final is normalised over the categories that have at least one scored item (default).</summary>
    Normalise,

    /// <summary>A missing score counts as 0 points.</summary>
    Zero,

    /// <summary>No final until every counted item has a score.</summary>
    HideUntilComplete,
}

/// <summary>How the items of a category combine (setting <c>Grades.CategoryMethod</c>).</summary>
public enum CategoryMethod
{
    /// <summary>Sum of points over sum of maximum points (default).</summary>
    PointsWeighted,

    /// <summary>The mean of the item percentages.</summary>
    PercentAverage,
}

/// <summary>A category and its weight (0 to 100). A weight of 0 never contributes to the final (the "Uncategorised" category).</summary>
public sealed record CalcCategory(Guid Id, decimal Weight);

/// <summary>
/// A graded item. <c>IsPublished</c> is true when students may see it; the engine drops the others when
/// <see cref="CalcRules.PublishedOnly"/> is set. <c>SourceKind</c> is carried for the caller and never interpreted, so a source the engine
/// has not heard of cannot break a calculation.
/// </summary>
public sealed record CalcItem(Guid Id, Guid CategoryId, decimal MaxPoints, bool IsPublished = true, int? SourceKind = null);

/// <summary>One revision of an event score (an assignment or quiz grade). Later revisions replace earlier ones.</summary>
public sealed record CalcEvent(Guid ItemId, Guid StudentId, int Revision, decimal Points);

/// <summary>
/// A teacher's entry: the points of a manual item, an override of an event score (<c>IsOverride</c>), or an excuse
/// (<c>IsExcused</c>: the item does not count for this student).
/// </summary>
public sealed record CalcEntry(Guid ItemId, Guid StudentId, decimal? Points, bool IsOverride = false, bool IsExcused = false);

/// <summary>A band of the grading scale: the lowest percentage (inclusive) that earns it.</summary>
public sealed record CalcBand(decimal MinPercent, string LabelAr, string LabelEn, decimal? GpaPoints = null);

/// <summary>The gradebook settings the calculation needs.</summary>
public sealed record CalcRules(
    MissingPolicy Missing = MissingPolicy.Normalise,
    CategoryMethod Method = CategoryMethod.PointsWeighted,
    int DecimalPlaces = 2,
    bool PublishedOnly = false);

/// <summary>
/// Everything the engine reads, as plain records: no entity, no I/O. <c>Students</c> names the students to calculate; when empty, every
/// student that appears in an event or an entry is calculated.
/// </summary>
public sealed record CalculationInput(
    IReadOnlyList<CalcCategory> Categories,
    IReadOnlyList<CalcItem> Items,
    IReadOnlyList<CalcEvent> Events,
    IReadOnlyList<CalcEntry> Entries,
    IReadOnlyList<CalcBand> Bands,
    CalcRules Rules,
    IReadOnlyList<Guid>? Students = null);
