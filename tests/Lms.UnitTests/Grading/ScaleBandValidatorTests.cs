using Lms.Domain.Grading;
using Lms.Domain.Organizations;

namespace Lms.UnitTests.Grading;

public sealed class ScaleBandValidatorTests
{
    private static ScaleBandInput B(decimal min, decimal max, string code, decimal? points = null) => new(0, min, max, code, points);

    private static BandValidation Check(bool points, params ScaleBandInput[] bands) => ScaleBandValidator.Validate(bands, points, 20);

    [Fact]
    public void Bands_must_cover_0_to_100_without_gaps_or_overlaps()
    {
        Assert.True(Check(false, B(0, 60, "F"), B(60, 100, "P")).IsValid);
        Assert.True(Check(false, B(60, 100, "P"), B(0, 60, "F")).IsValid); // order of the list does not matter
    }

    [Fact]
    public void A_gap_is_reported_with_its_range()
    {
        var result = Check(false, B(0, 50, "F"), B(60, 100, "P"));

        var problem = Assert.Single(result.Problems);
        Assert.Equal((ScaleBandValidator.Gap, 50m, 60m), (problem.Kind, problem.From, problem.To));
    }

    [Fact]
    public void A_scale_that_does_not_reach_100_or_start_at_0_has_gaps()
    {
        Assert.Contains(Check(false, B(10, 100, "P")).Problems, p => p is { Kind: ScaleBandValidator.Gap, From: 0, To: 10 });
        Assert.Contains(Check(false, B(0, 90, "P")).Problems, p => p is { Kind: ScaleBandValidator.Gap, From: 90, To: 100 });
    }

    [Fact]
    public void An_overlap_is_reported()
    {
        var result = Check(false, B(0, 70, "F"), B(60, 100, "P"));

        Assert.Contains(result.Problems, p => p is { Kind: ScaleBandValidator.Overlap, From: 60, To: 70 });
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, 50)]
    [InlineData(60, 50)]
    [InlineData(0, 101)]
    public void A_band_with_a_bad_range_is_refused(double min, double max) =>
        Assert.Contains(Check(false, B((decimal)min, (decimal)max, "X")).Problems, p => p.Kind == ScaleBandValidator.Range);

    [Fact]
    public void Codes_are_required_and_unique_ignoring_case()
    {
        Assert.Contains(Check(false, B(0, 50, ""), B(50, 100, "P")).Problems, p => p.Kind == ScaleBandValidator.Code);
        Assert.Contains(Check(false, B(0, 50, "a"), B(50, 100, "A")).Problems, p => p.Kind == ScaleBandValidator.Code);
    }

    [Fact]
    public void A_gpa_scale_needs_points_on_every_band()
    {
        Assert.Contains(Check(true, B(0, 60, "F", 0m), B(60, 100, "P")).Problems, p => p.Kind == ScaleBandValidator.Points);
        Assert.True(Check(true, B(0, 60, "F", 0m), B(60, 100, "P", 4m)).IsValid);
    }

    [Fact]
    public void No_bands_and_too_many_bands_are_refused()
    {
        Assert.Contains(Check(false).Problems, p => p.Kind == ScaleBandValidator.Count);
        var many = Enumerable.Range(0, 21).Select(i => B(i * 4, i * 4 + 4, "C" + i)).ToArray();
        Assert.Contains(ScaleBandValidator.Validate(many, false, 20).Problems, p => p.Kind == ScaleBandValidator.Count);
    }

    [Fact]
    public void Band_lookup_uses_the_minimum_inclusive()
    {
        var bands = new[] { B(0, 60, "F"), B(60, 90, "P"), B(90, 100, "A") };

        Assert.Equal("P", ScaleBandValidator.Find(bands, 60m, b => b.MinPercent)!.Code);
        Assert.Equal("F", ScaleBandValidator.Find(bands, 59.99m, b => b.MinPercent)!.Code);
        Assert.Equal("A", ScaleBandValidator.Find(bands, 100m, b => b.MinPercent)!.Code);
    }

    [Fact]
    public void Every_saudi_preset_is_a_valid_scale()
    {
        Assert.Equal(3, SaudiPresetSeed.All.Count);
        Assert.All(SaudiPresetSeed.All, seed =>
        {
            var bands = seed.Bands.Select((b, i) => new ScaleBandInput(i, b.Min, b.Max, b.Code, b.Points)).ToList();
            Assert.True(ScaleBandValidator.Validate(bands, seed.Kind == "Gpa", 20).IsValid, seed.Key);
            Assert.All(seed.Bands, b => Assert.False(string.IsNullOrWhiteSpace(b.LabelAr) || string.IsNullOrWhiteSpace(b.LabelEn)));
        });
    }

    [Fact]
    public void Preset_numbers_follow_the_published_tables_and_the_5_point_scale_is_the_university_suggestion()
    {
        var school = SaudiPresetSeed.Find(SaudiPresetSeed.SchoolKey)!;
        var five = SaudiPresetSeed.Find(SaudiPresetSeed.University5Key)!;
        var four = SaudiPresetSeed.Find(SaudiPresetSeed.University4Key)!;

        Assert.Equal([90m, 80m, 70m, 60m, 0m], school.Bands.Select(b => b.Min));
        Assert.Equal([5.00m, 4.75m, 4.50m, 4.00m, 3.50m, 3.00m, 2.50m, 2.00m, 1.00m], five.Bands.Select(b => b.Points!.Value));
        Assert.Equal([4.00m, 3.75m, 3.50m, 3.00m, 2.50m, 2.00m, 1.50m, 1.00m, 0.00m], four.Bands.Select(b => b.Points!.Value));
        Assert.Equal(OrganizationType.University, five.DefaultSuggestionFor);
        Assert.Null(school.DefaultSuggestionFor);
        Assert.Null(four.DefaultSuggestionFor);
        Assert.All(SaudiPresetSeed.All, s => Assert.True(s.Version >= 1));
    }
}
