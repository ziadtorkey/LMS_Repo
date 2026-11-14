namespace Lms.Domain.Grading.Calculation;

/// <summary>
/// The gradebook calculation (FR-GRD-01, FR-GRD-03, D58): a pure function from plain records to results, with no entity, no I/O and no
/// clock, so the grid, the student and guardian reads and the reports all compute with the same code.
/// <list type="number">
/// <item>An item's score for a student: excused drops the item; else an override entry; else a manual entry; else the event with the
/// highest revision; else the item is missing. Items without maximum points are ignored.</item>
/// <item>A category is the points-weighted or the percent-average result of its counted items. Under the <c>Zero</c> policy a missing item
/// counts as 0; otherwise only scored items count, and a category with none does not take part.</item>
/// <item>The final is the weight-weighted mean of the participating categories (a weight of 0 never contributes), so it is normalised over
/// the categories that have scores. <c>HideUntilComplete</c> gives no final while an item of a weighted category is missing.</item>
/// <item>The final is rounded half-up once, at the end; the band is looked up from the rounded percentage.</item>
/// </list>
/// Weights outside 0-100 or not totalling 100 make the result <c>IsValid = false</c> with a warning (culture-invariant text) but never
/// stop the calculation. Scores are never clamped: points above the maximum give a percentage above 100 (extra credit is implicit) and
/// negative points give a negative percentage, so a caller that must refuse them validates on entry (L12), not here.
/// </summary>
public static class GradebookCalculator
{
    private const decimal WeightTolerance = 0.0001m;

    public static CalculationResult Calculate(CalculationInput input)
    {
        var rules = input.Rules;
        var warnings = ValidateWeights(input.Categories);
        var weights = input.Categories.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Weight);
        var items = input.Items
            .Where(i => i.MaxPoints > 0 && (!rules.PublishedOnly || i.IsPublished))
            .ToList();

        var latestEvent = input.Events
            .GroupBy(e => (e.ItemId, e.StudentId))
            .ToDictionary(g => g.Key, g => g.MaxBy(e => e.Revision)!);
        var entries = input.Entries.GroupBy(e => (e.ItemId, e.StudentId)).ToDictionary(g => g.Key, g => g.ToList());
        var bands = input.Bands.OrderByDescending(b => b.MinPercent).ToList();

        var students = input.Students is { Count: > 0 } named
            ? named.Distinct()
            : input.Events.Select(e => e.StudentId).Concat(input.Entries.Select(e => e.StudentId)).Distinct().Order();

        var results = students
            .Select(student => CalculateStudent(student, items, input.Categories, weights, latestEvent, entries, bands, rules))
            .ToList();
        return new CalculationResult(warnings.Count == 0, warnings, results);
    }

    private static StudentResult CalculateStudent(
        Guid student, List<CalcItem> items, IReadOnlyList<CalcCategory> categories, Dictionary<Guid, decimal> weights,
        Dictionary<(Guid ItemId, Guid StudentId), CalcEvent> events, Dictionary<(Guid ItemId, Guid StudentId), List<CalcEntry>> entries,
        List<CalcBand> bands, CalcRules rules)
    {
        var itemResults = items.Select(item => ItemOf(student, item, events, entries)).ToList();
        var maxOf = items.ToDictionary(i => i.Id, i => i.MaxPoints);

        var categoryResults = new List<CategoryResult>();
        foreach (var category in categories.GroupBy(c => c.Id).Select(g => g.First()))
        {
            var counted = items.Where(i => i.CategoryId == category.Id)
                .Select(i => itemResults.First(r => r.ItemId == i.Id))
                .Where(r => r.Source != ItemSource.Excused)
                .ToList();
            var included = rules.Missing == MissingPolicy.Zero ? counted : counted.Where(r => r.Source != ItemSource.Missing).ToList();
            decimal? percent = included.Count == 0 ? null : CategoryPercent(included, maxOf, rules.Method);
            categoryResults.Add(new CategoryResult(category.Id, percent, Participates: percent is not null && category.Weight > 0));
        }

        var participating = categoryResults.Where(c => c.Participates).ToList();
        var totalWeight = participating.Sum(c => weights[c.CategoryId]);

        // Complete = every counted item of a weighted category has a score (an item of a weight-0 category never blocks the total).
        var weighted = categories.Where(c => c.Weight > 0).Select(c => c.Id).ToHashSet();
        var complete = !items.Where(i => weighted.Contains(i.CategoryId))
            .Any(i => itemResults.First(r => r.ItemId == i.Id).Source == ItemSource.Missing);

        decimal? final = null;
        if (totalWeight > 0 && !(rules.Missing == MissingPolicy.HideUntilComplete && !complete))
        {
            final = Rounding.HalfUp(participating.Sum(c => c.Percent!.Value * weights[c.CategoryId]) / totalWeight, rules.DecimalPlaces);
        }

        var band = final is { } rounded ? bands.FirstOrDefault(b => rounded >= b.MinPercent) : null;
        return new StudentResult(student, final, band, complete, itemResults, categoryResults);
    }

    private static ItemResult ItemOf(
        Guid student, CalcItem item, Dictionary<(Guid ItemId, Guid StudentId), CalcEvent> events,
        Dictionary<(Guid ItemId, Guid StudentId), List<CalcEntry>> entries)
    {
        var key = (item.Id, student);
        if (entries.TryGetValue(key, out var mine))
        {
            if (mine.Any(e => e.IsExcused))
            {
                return new ItemResult(item.Id, ItemSource.Excused, null, null);
            }

            if (mine.LastOrDefault(e => e.IsOverride && e.Points is not null) is { } over)
            {
                return Scored(item, ItemSource.Override, over.Points!.Value);
            }

            if (mine.LastOrDefault(e => !e.IsOverride && e.Points is not null) is { } manual)
            {
                return Scored(item, ItemSource.Manual, manual.Points!.Value);
            }
        }

        return events.TryGetValue(key, out var scored)
            ? Scored(item, ItemSource.Event, scored.Points)
            : new ItemResult(item.Id, ItemSource.Missing, null, null);
    }

    private static ItemResult Scored(CalcItem item, ItemSource source, decimal points) =>
        new(item.Id, source, points, points / item.MaxPoints * 100m);

    private static decimal CategoryPercent(List<ItemResult> included, Dictionary<Guid, decimal> maxOf, CategoryMethod method)
    {
        if (method == CategoryMethod.PercentAverage)
        {
            return included.Average(r => (r.Points ?? 0m) / maxOf[r.ItemId] * 100m);
        }

        return included.Sum(r => r.Points ?? 0m) / included.Sum(r => maxOf[r.ItemId]) * 100m;
    }

    private static List<string> ValidateWeights(IReadOnlyList<CalcCategory> categories)
    {
        var warnings = new List<string>();
        if (categories.Any(c => c.Weight is < 0m or > 100m))
        {
            warnings.Add("A category weight is outside 0 to 100.");
        }

        var total = categories.Sum(c => c.Weight);
        if (Math.Abs(total - 100m) > WeightTolerance)
        {
            warnings.Add($"The category weights total {total.ToString(System.Globalization.CultureInfo.InvariantCulture)} and not 100.");
        }

        return warnings;
    }
}
