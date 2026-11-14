namespace Lms.Domain.Grading.Calculation;

/// <summary>Where an item's score for a student came from (the grid's cell state, minus the staff-only newer-score marker).</summary>
public enum ItemSource
{
    Event,
    Manual,
    Override,
    Excused,
    Missing,
}

/// <summary>One item for one student. <c>Points</c> and <c>Percent</c> are null when the item is excused or missing.</summary>
public sealed record ItemResult(Guid ItemId, ItemSource Source, decimal? Points, decimal? Percent);

/// <summary>A category for one student; <c>Percent</c> is null when none of its items count.</summary>
public sealed record CategoryResult(Guid CategoryId, decimal? Percent, bool Participates);

/// <summary>
/// One student's result. <c>FinalPercent</c> is rounded once, at the end; it is null when nothing is graded, or when the missing policy is
/// <c>HideUntilComplete</c> and an item is still missing (<c>IsComplete</c> false). <c>Band</c> is looked up from the rounded percentage.
/// </summary>
public sealed record StudentResult(
    Guid StudentId,
    decimal? FinalPercent,
    CalcBand? Band,
    bool IsComplete,
    IReadOnlyList<ItemResult> Items,
    IReadOnlyList<CategoryResult> Categories);

/// <summary>
/// The calculation of a whole gradebook. <c>IsValid</c> is false when the category weights are out of range or do not total 100: the
/// results are still computed (a teacher can keep entering grades) and <c>Warnings</c> says why.
/// </summary>
public sealed record CalculationResult(bool IsValid, IReadOnlyList<string> Warnings, IReadOnlyList<StudentResult> Students);
