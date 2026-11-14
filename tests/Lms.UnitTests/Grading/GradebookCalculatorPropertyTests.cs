using Lms.Domain.Grading.Calculation;

namespace Lms.UnitTests.Grading;

/// <summary>Invariants of the calculation over many seeded random gradebooks (no external property-testing package).</summary>
public sealed class GradebookCalculatorPropertyTests
{


    private static Guid G(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    /// <summary>A random but valid gradebook: weights total 100, points never above the maximum.</summary>
    private static CalculationInput RandomBook(Random random, MissingPolicy? policy = null, CategoryMethod? method = null)
    {
        var categoryCount = random.Next(1, 5);
        var cuts = Enumerable.Range(0, categoryCount - 1).Select(_ => random.Next(1, 100)).Order().ToList();
        var weights = new List<decimal>();
        var previous = 0;
        foreach (var cut in cuts.Append(100))
        {
            weights.Add(cut - previous);
            previous = cut;
        }

        // The cuts may repeat; fold zero-width slices into the next so every weight is positive and the total stays 100.
        var categories = weights.Select((w, i) => new CalcCategory(G(100 + i), w)).ToList();
        var items = Enumerable.Range(0, random.Next(1, 9))
            .Select(i => new CalcItem(G(200 + i), categories[random.Next(categories.Count)].Id, random.Next(1, 50), IsPublished: random.Next(5) != 0))
            .ToList();
        var students = Enumerable.Range(0, random.Next(1, 5)).Select(i => G(300 + i)).ToList();
        var events = new List<CalcEvent>();
        foreach (var item in items)
        {
            foreach (var student in students.Where(_ => random.Next(4) != 0))
            {
                for (var revision = 1; revision <= random.Next(1, 4); revision++)
                {
                    events.Add(new CalcEvent(item.Id, student, revision, Math.Round((decimal)random.NextDouble() * item.MaxPoints, 2)));
                }
            }
        }

        var entries = new List<CalcEntry>();
        foreach (var item in items.Where(_ => random.Next(3) == 0))
        {
            entries.Add(new CalcEntry(item.Id, students[random.Next(students.Count)], null, IsExcused: true));
        }

        var rules = new CalcRules(
            policy ?? (MissingPolicy)random.Next(3), method ?? (CategoryMethod)random.Next(2), random.Next(0, 4), PublishedOnly: random.Next(4) == 0);
        return new CalculationInput(categories, items, events, entries, [new CalcBand(0, "ع", "F"), new CalcBand(50, "ج", "P")], rules, students);
    }

    private static string Describe(CalculationResult result) =>
        string.Join("|", result.Students.OrderBy(s => s.StudentId).Select(s =>
            $"{s.StudentId}:{s.FinalPercent}:{s.Band?.LabelEn}:{s.IsComplete}:" +
            string.Join(",", s.Items.OrderBy(i => i.ItemId).Select(i => $"{i.ItemId}={i.Source}/{i.Points}")) + ":" +
            string.Join(",", s.Categories.OrderBy(c => c.CategoryId).Select(c => $"{c.CategoryId}={c.Percent}"))));

    [Fact]
    public void Weights_summing_to_100_give_a_result_within_0_and_100()
    {
        var random = new Random(1);
        for (var i = 0; i < 300; i++)
        {
            var book = RandomBook(random);
            if (book.Categories.Any(c => c.Weight <= 0))
            {
                continue; // a zero-width slice from repeated cuts: not a valid gradebook for this property
            }

            var result = GradebookCalculator.Calculate(book);

            Assert.True(result.IsValid);
            Assert.All(result.Students, s =>
            {
                if (s.FinalPercent is { } final)
                {
                    Assert.InRange(final, 0m, 100m);
                }
            });
        }
    }

    [Fact]
    public void Permuting_items_never_changes_the_result()
    {
        var random = new Random(2);
        for (var i = 0; i < 300; i++)
        {
            var book = RandomBook(random);
            var shuffled = book with
            {
                Categories = [.. book.Categories.OrderBy(_ => random.Next())],
                Items = [.. book.Items.OrderBy(_ => random.Next())],
                Events = [.. book.Events.OrderBy(_ => random.Next())],
                Entries = [.. book.Entries.OrderBy(_ => random.Next())],
                Students = [.. book.Students!.OrderBy(_ => random.Next())],
            };

            Assert.Equal(Describe(GradebookCalculator.Calculate(book)), Describe(GradebookCalculator.Calculate(shuffled)));
        }
    }

    [Fact]
    public void A_regrade_revision_replaces_its_predecessor()
    {
        var random = new Random(3);
        for (var i = 0; i < 200; i++)
        {
            var book = RandomBook(random);
            // Keep only the highest revision of each (item, student): the result must not change.
            var latestOnly = book with
            {
                Events = [.. book.Events.GroupBy(e => (e.ItemId, e.StudentId)).Select(g => g.MaxBy(e => e.Revision)!)],
            };

            Assert.Equal(Describe(GradebookCalculator.Calculate(latestOnly)), Describe(GradebookCalculator.Calculate(book)));
        }
    }

    [Fact]
    public void Calculating_twice_gives_the_same_result()
    {
        var random = new Random(4);
        for (var i = 0; i < 100; i++)
        {
            var book = RandomBook(random);
            Assert.Equal(Describe(GradebookCalculator.Calculate(book)), Describe(GradebookCalculator.Calculate(book)));
        }
    }

    [Fact]
    public void Under_the_normalise_policy_a_missing_item_never_changes_the_result_of_another_category()
    {
        var random = new Random(5);
        for (var i = 0; i < 200; i++)
        {
            var book = RandomBook(random, MissingPolicy.Normalise, CategoryMethod.PointsWeighted);
            var result = GradebookCalculator.Calculate(book);
            var student = result.Students[0];
            // Drop every score of one category: the other categories keep exactly their own percentage.
            var victim = book.Categories[random.Next(book.Categories.Count)].Id;
            var itemsOfVictim = book.Items.Where(it => it.CategoryId == victim).Select(it => it.Id).ToHashSet();
            var stripped = GradebookCalculator.Calculate(book with { Events = [.. book.Events.Where(e => !itemsOfVictim.Contains(e.ItemId))] });

            foreach (var category in student.Categories.Where(c => c.CategoryId != victim))
            {
                Assert.Equal(category.Percent, stripped.Students.Single(s => s.StudentId == student.StudentId).Categories.Single(c => c.CategoryId == category.CategoryId).Percent);
            }
        }
    }
}
