using Lms.Domain.Grading.Calculation;

namespace Lms.UnitTests.Grading;

public sealed class GradebookCalculatorTests
{
    private static readonly Guid Student = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Cat1 = Guid.Parse("00000000-0000-0000-0000-00000000c001");
    private static readonly Guid Cat2 = Guid.Parse("00000000-0000-0000-0000-00000000c002");
    private static readonly Guid Item1 = Guid.Parse("00000000-0000-0000-0000-00000000e001");
    private static readonly Guid Item2 = Guid.Parse("00000000-0000-0000-0000-00000000e002");
    private static readonly Guid Item3 = Guid.Parse("00000000-0000-0000-0000-00000000e003");

    internal static readonly CalcBand[] SchoolBands =
    [
        new(0m, "راسب", "Fail"), new(60m, "مقبول", "Acceptable"), new(70m, "جيد", "Good"), new(80m, "جيد جدا", "Very Good"),
        new(90m, "ممتاز", "Excellent"),
    ];

    private static CalculationInput Input(
        IEnumerable<CalcCategory> categories, IEnumerable<CalcItem> items, IEnumerable<CalcEvent>? events = null,
        IEnumerable<CalcEntry>? entries = null, CalcRules? rules = null, IEnumerable<CalcBand>? bands = null, IEnumerable<Guid>? students = null) =>
        new([.. categories], [.. items], [.. events ?? []], [.. entries ?? []], [.. bands ?? SchoolBands], rules ?? new CalcRules(),
            students is null ? null : [.. students]);

    private static StudentResult Only(CalculationResult result) => Assert.Single(result.Students);

    // Two categories (60 / 40): tests of 100 points and homework of 20 points.
    private static CalculationInput TwoCategories(CalcRules? rules = null, IEnumerable<CalcEvent>? events = null, IEnumerable<CalcEntry>? entries = null) =>
        Input(
            [new(Cat1, 60m), new(Cat2, 40m)],
            [new(Item1, Cat1, 100m), new(Item2, Cat2, 20m), new(Item3, Cat2, 30m)],
            events, entries, rules);

    [Fact]
    public void Weights_summing_to_100_give_a_result_within_0_and_100()
    {
        var result = GradebookCalculator.Calculate(TwoCategories(
            events: [new(Item1, Student, 1, 80m), new(Item2, Student, 1, 20m), new(Item3, Student, 1, 30m)]));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
        // category 1 = 80 %, category 2 = 100 %: 0.6 * 80 + 0.4 * 100 = 88
        Assert.Equal(88m, Only(result).FinalPercent);
    }

    [Fact]
    public void A_regrade_revision_replaces_its_predecessor()
    {
        var result = GradebookCalculator.Calculate(TwoCategories(
            events: [new(Item1, Student, 1, 40m), new(Item1, Student, 3, 90m), new(Item1, Student, 2, 70m)]));

        var item = Only(result).Items.Single(i => i.ItemId == Item1);
        Assert.Equal((ItemSource.Event, 90m), (item.Source, item.Points)); // the highest revision, whatever the order of the list
    }

    [Fact]
    public void Override_wins_over_the_event()
    {
        var result = GradebookCalculator.Calculate(TwoCategories(
            events: [new(Item1, Student, 1, 40m)],
            entries: [new(Item1, Student, 75m, IsOverride: true)]));

        var item = Only(result).Items.Single(i => i.ItemId == Item1);
        Assert.Equal((ItemSource.Override, 75m), (item.Source, item.Points));
    }

    [Fact]
    public void A_manual_score_is_read_from_the_entry()
    {
        var result = GradebookCalculator.Calculate(TwoCategories(entries: [new(Item1, Student, 64m)]));

        var item = Only(result).Items.Single(i => i.ItemId == Item1);
        Assert.Equal((ItemSource.Manual, 64m), (item.Source, item.Points));
    }

    [Fact]
    public void Excused_items_drop_out_for_that_student_only()
    {
        var other = Guid.NewGuid();
        var result = GradebookCalculator.Calculate(TwoCategories(
            events: [new(Item1, Student, 1, 50m), new(Item2, Student, 1, 0m), new(Item3, Student, 1, 30m),
                     new(Item1, other, 1, 50m), new(Item2, other, 1, 0m), new(Item3, other, 1, 30m)],
            entries: [new(Item2, Student, null, IsExcused: true)]));

        var excused = result.Students.Single(s => s.StudentId == Student);
        var normal = result.Students.Single(s => s.StudentId == other);
        Assert.Equal(ItemSource.Excused, excused.Items.Single(i => i.ItemId == Item2).Source);
        Assert.True(excused.FinalPercent > normal.FinalPercent); // the zero no longer counts
    }

    [Fact]
    public void Excused_items_never_lower_the_result()
    {
        // Marking an item that scored 0 (or is missing under the Zero policy) as excused cannot lower any final.
        var random = new Random(42);
        for (var round = 0; round < 200; round++)
        {
            var policy = random.Next(2) == 0 ? MissingPolicy.Zero : MissingPolicy.Normalise;
            var rules = new CalcRules(policy);
            var events = new List<CalcEvent>();
            foreach (var item in new[] { Item1, Item2, Item3 })
            {
                if (random.Next(4) != 0)
                {
                    events.Add(new CalcEvent(item, Student, 1, random.Next(0, 3) == 0 ? 0m : random.Next(1, 20)));
                }
            }

            var zeroOrMissing = new[] { Item1, Item2, Item3 }.Where(i => events.All(e => e.ItemId != i || e.Points == 0m)).ToList();
            foreach (var target in zeroOrMissing)
            {
                var before = Only(GradebookCalculator.Calculate(TwoCategories(rules, events) with { Students = [Student] })).FinalPercent;
                var after = Only(GradebookCalculator.Calculate(TwoCategories(rules, events, [new CalcEntry(target, Student, null, IsExcused: true)]) with { Students = [Student] })).FinalPercent;
                Assert.True(after is null || before is null || after >= before, $"round {round}: {before} -> {after} excusing {target}");
            }
        }
    }

    [Theory]
    [InlineData(MissingPolicy.Normalise, 80.0)]          // only category 1 is graded: its 80 % is the final
    [InlineData(MissingPolicy.Zero, 48.0)]               // category 2 counts as 0: 0.6 * 80 + 0.4 * 0
    [InlineData(MissingPolicy.HideUntilComplete, null)]
    public void Missing_policy_normalise_zero_and_hide(MissingPolicy policy, double? expected)
    {
        var result = GradebookCalculator.Calculate(TwoCategories(new CalcRules(policy), events: [new(Item1, Student, 1, 80m)]));

        var student = Only(result);
        Assert.Equal(expected is null ? null : (decimal)expected, student.FinalPercent);
        Assert.False(student.IsComplete); // two items still have no score, whatever the policy does with them
    }

    [Fact]
    public void Hide_until_complete_shows_the_final_once_every_item_has_a_score()
    {
        var result = GradebookCalculator.Calculate(TwoCategories(new CalcRules(MissingPolicy.HideUntilComplete),
            events: [new(Item1, Student, 1, 80m), new(Item2, Student, 1, 10m), new(Item3, Student, 1, 15m)]));

        var student = Only(result);
        Assert.True(student.IsComplete);
        Assert.Equal(68m, student.FinalPercent); // 0.6 * 80 + 0.4 * (25/50 = 50 %)
    }

    [Fact]
    public void Category_method_points_weighted_vs_percent_average()
    {
        var events = new CalcEvent[] { new(Item2, Student, 1, 10m), new(Item3, Student, 1, 30m) }; // 50 % of 20 and 100 % of 30

        var pointsWeighted = GradebookCalculator.Calculate(TwoCategories(new CalcRules(Method: CategoryMethod.PointsWeighted), events));
        var percentAverage = GradebookCalculator.Calculate(TwoCategories(new CalcRules(Method: CategoryMethod.PercentAverage), events));

        Assert.Equal(80m, Only(pointsWeighted).FinalPercent);  // (10 + 30) / (20 + 30)
        Assert.Equal(75m, Only(percentAverage).FinalPercent);  // (50 % + 100 %) / 2
    }

    [Fact]
    public void Rounding_is_half_up_once_at_the_end()
    {
        // 2 of 3 points in a single category: 66.666...; with 2 places 66.67, with 0 places 67, and 0.5 goes up.
        var input = (int places) => Input([new(Cat1, 100m)], [new(Item1, Cat1, 3m)], [new(Item1, Student, 1, 2m)], rules: new CalcRules(DecimalPlaces: places));

        Assert.Equal(66.67m, Only(GradebookCalculator.Calculate(input(2))).FinalPercent);
        Assert.Equal(67m, Only(GradebookCalculator.Calculate(input(0))).FinalPercent);

        var half = Input([new(Cat1, 100m)], [new(Item1, Cat1, 8m)], [new(Item1, Student, 1, 5m)], rules: new CalcRules(DecimalPlaces: 0)); // 62.5
        Assert.Equal(63m, Only(GradebookCalculator.Calculate(half)).FinalPercent);

        // Once at the end: categories of 62.5 % and 62.4 % at 50/50 give 62.45, which is 62 with no places. Rounding each category first
        // (63 and 62) would give 62.5 and then 63.
        var late = Input([new(Cat1, 50m), new(Cat2, 50m)], [new(Item1, Cat1, 100m), new(Item2, Cat2, 100m)],
            [new(Item1, Student, 1, 62.5m), new(Item2, Student, 1, 62.4m)], rules: new CalcRules(DecimalPlaces: 0));
        Assert.Equal(62m, Only(GradebookCalculator.Calculate(late)).FinalPercent);
    }

    [Fact]
    public void Published_only_ignores_hidden_and_scheduled_items()
    {
        var items = new CalcItem[] { new(Item1, Cat1, 100m, IsPublished: true), new(Item2, Cat1, 100m, IsPublished: false) };
        var events = new CalcEvent[] { new(Item1, Student, 1, 90m), new(Item2, Student, 1, 10m) };

        var all = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], items, events));
        var published = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], items, events, rules: new CalcRules(PublishedOnly: true)));

        Assert.Equal(50m, Only(all).FinalPercent);
        Assert.Equal(90m, Only(published).FinalPercent);
        Assert.DoesNotContain(Only(published).Items, i => i.ItemId == Item2); // the hidden item is not even listed
    }

    [Fact]
    public void Invalid_weights_compute_with_a_warning()
    {
        var result = GradebookCalculator.Calculate(Input(
            [new(Cat1, 70m), new(Cat2, 20m)], [new(Item1, Cat1, 100m), new(Item2, Cat2, 100m)],
            [new(Item1, Student, 1, 80m), new(Item2, Student, 1, 40m)]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("100"));
        // still computed, normalised over the weights that exist: (0.7 * 80 + 0.2 * 40) / 0.9
        Assert.Equal(71.11m, Only(result).FinalPercent);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(101)]
    public void A_weight_out_of_range_is_invalid_but_never_throws(int weight)
    {
        var result = GradebookCalculator.Calculate(Input([new(Cat1, weight)], [new(Item1, Cat1, 10m)], [new(Item1, Student, 1, 5m)]));

        Assert.False(result.IsValid);
        Assert.Single(result.Students);
    }

    [Fact]
    public void Unknown_source_kind_does_not_throw()
    {
        var result = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], [new(Item1, Cat1, 10m, SourceKind: 9999)], [new(Item1, Student, 1, 5m)]));

        Assert.Equal(50m, Only(result).FinalPercent);
    }

    [Theory]
    [InlineData(0, "Fail")]
    [InlineData(59.99, "Fail")]
    [InlineData(60, "Acceptable")]   // the lower bound is inclusive
    [InlineData(69.99, "Acceptable")]
    [InlineData(70, "Good")]
    [InlineData(89.99, "Very Good")]
    [InlineData(90, "Excellent")]
    [InlineData(100, "Excellent")]
    public void Band_lookup_uses_min_percent_inclusive(double percent, string expected)
    {
        var result = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], [new(Item1, Cat1, 100m)], [new(Item1, Student, 1, (decimal)percent)]));

        Assert.Equal(expected, Only(result).Band!.LabelEn);
    }

    [Fact]
    public void The_band_follows_the_rounded_percentage()
    {
        // 89.996 rounds to 90.00, which is the lowest percentage of Excellent: the student sees 90.00, so the band must agree.
        var result = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], [new(Item1, Cat1, 1000m)], [new(Item1, Student, 1, 899.96m)]));

        Assert.Equal((90m, "Excellent"), (Only(result).FinalPercent, Only(result).Band!.LabelEn));
    }

    [Fact]
    public void An_item_with_no_maximum_points_is_ignored_instead_of_dividing_by_zero()
    {
        var result = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], [new(Item1, Cat1, 0m), new(Item2, Cat1, 10m)],
            [new(Item1, Student, 1, 5m), new(Item2, Student, 1, 5m)]));

        Assert.Equal(50m, Only(result).FinalPercent);
    }

    [Fact]
    public void An_all_excused_category_does_not_take_part_and_the_others_carry_the_final()
    {
        var result = GradebookCalculator.Calculate(TwoCategories(
            events: [new(Item1, Student, 1, 80m)],
            entries: [new(Item2, Student, null, IsExcused: true), new(Item3, Student, null, IsExcused: true)]));

        var student = Only(result);
        Assert.Equal(80m, student.FinalPercent); // category 2 has no counted item, so category 1 is normalised to the whole
        Assert.False(student.Categories.Single(c => c.CategoryId == Cat2).Participates);
        Assert.True(student.IsComplete);          // excused items are not missing
    }

    [Fact]
    public void An_empty_gradebook_gives_no_students_and_never_throws()
    {
        var result = GradebookCalculator.Calculate(Input([], [], bands: []));

        Assert.Empty(result.Students);
        Assert.False(result.IsValid); // no categories: the weights total 0, not 100
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Points_above_the_maximum_are_not_clamped_so_extra_credit_is_implicit()
    {
        var result = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], [new(Item1, Cat1, 10m)], [new(Item1, Student, 1, 12m)]));

        Assert.Equal(120m, Only(result).FinalPercent);
        Assert.Equal(120m, Only(result).Items.Single().Percent);
    }

    [Fact]
    public void Negative_points_are_not_clamped_either()
    {
        var result = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], [new(Item1, Cat1, 10m)], [new(Item1, Student, 1, -2m)]));

        Assert.Equal(-20m, Only(result).FinalPercent);
        Assert.Null(Only(result).Band); // below the lowest band (min 0): no band; entry validation (L12) keeps points non-negative
    }

    [Fact]
    public void The_result_and_the_warning_do_not_depend_on_the_culture_of_the_thread()
    {
        var input = Input([new(Cat1, 70m), new(Cat2, 20.5m)], [new(Item1, Cat1, 100m)], [new(Item1, Student, 1, 83.456m)]);
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            var english = GradebookCalculator.Calculate(input);
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            var arabic = GradebookCalculator.Calculate(input);

            Assert.Equal(english.Warnings, arabic.Warnings);
            Assert.Contains("90.5", arabic.Warnings.Single(w => w.Contains("total")));
            Assert.Equal(english.Students.Single().FinalPercent, arabic.Students.Single().FinalPercent);
            Assert.Equal(83.46m, arabic.Students.Single().FinalPercent);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_student_with_no_scores_has_no_final_and_no_band()
    {
        var result = GradebookCalculator.Calculate(Input([new(Cat1, 100m)], [new(Item1, Cat1, 10m)], students: [Student]));

        var student = Only(result);
        Assert.Null(student.FinalPercent);
        Assert.Null(student.Band);
    }
}
