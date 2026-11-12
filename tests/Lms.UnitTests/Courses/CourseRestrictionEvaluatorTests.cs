using Lms.Domain.Courses;

namespace Lms.UnitTests.Courses;

public sealed class CourseRestrictionEvaluatorTests
{
    private static readonly Guid LevelA = Guid.NewGuid();
    private static readonly Guid LevelB = Guid.NewGuid();

    private static IReadOnlyList<(string Type, IReadOnlyList<string> Values)> R(params (string, string[])[] items) =>
        items.Select(i => (i.Item1, (IReadOnlyList<string>)i.Item2)).ToList();

    [Fact]
    public void No_restriction_passes_everyone()
    {
        Assert.Empty(CourseRestrictionEvaluator.Failed(R(), "female", "christian", LevelA));
    }

    [Fact]
    public void Religion_restriction_passes_only_listed_values()
    {
        var restrictions = R((CourseRestrictionEvaluator.Religion, ["muslim"]));

        Assert.Empty(CourseRestrictionEvaluator.Failed(restrictions, "male", "muslim", LevelA));
        Assert.Equal([CourseRestrictionEvaluator.Religion], CourseRestrictionEvaluator.Failed(restrictions, "male", "christian", LevelA));
    }

    [Fact]
    public void Any_of_within_a_type()
    {
        var restrictions = R((CourseRestrictionEvaluator.Religion, ["muslim", "christian"]));

        Assert.Empty(CourseRestrictionEvaluator.Failed(restrictions, "male", "christian", LevelA));
    }

    [Fact]
    public void Two_types_are_ANDed()
    {
        var restrictions = R(
            (CourseRestrictionEvaluator.Religion, ["muslim"]),
            (CourseRestrictionEvaluator.Gender, ["female"]));

        Assert.Empty(CourseRestrictionEvaluator.Failed(restrictions, "female", "muslim", LevelA));
        Assert.Equal([CourseRestrictionEvaluator.Gender], CourseRestrictionEvaluator.Failed(restrictions, "male", "muslim", LevelA));
        Assert.Equal(
            [CourseRestrictionEvaluator.Religion, CourseRestrictionEvaluator.Gender],
            CourseRestrictionEvaluator.Failed(restrictions, "male", "christian", LevelA));
    }

    [Fact]
    public void Level_type_restriction_compares_the_class_unit_level_type()
    {
        var restrictions = R((CourseRestrictionEvaluator.LevelType, [LevelA.ToString()]));

        Assert.Empty(CourseRestrictionEvaluator.Failed(restrictions, "male", "muslim", LevelA));
        Assert.Equal([CourseRestrictionEvaluator.LevelType], CourseRestrictionEvaluator.Failed(restrictions, "male", "muslim", LevelB));
    }

    [Fact]
    public void A_type_with_no_values_is_ignored_and_an_unknown_type_fails()
    {
        Assert.Empty(CourseRestrictionEvaluator.Failed(R((CourseRestrictionEvaluator.Gender, [])), "male", "muslim", LevelA));
        Assert.Equal(["Nonsense"], CourseRestrictionEvaluator.Failed(R(("Nonsense", ["x"])), "male", "muslim", LevelA));
    }
}
