using Lms.Domain.Packages;
using Lms.Infrastructure.Packages.Attempts;
using Lms.Shared.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>Which completed attempt the gradebook holds (D57): Highest, Last or First.</summary>
public sealed class PackageGradingTests
{
    private static readonly DateTime T0 = new(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);

    private static GradedAttempt A(int minutes, decimal? raw, decimal? max = 100m) =>
        new(Guid.NewGuid(), T0.AddMinutes(minutes), raw, max);

    [Fact]
    public void No_completed_attempt_grades_nothing()
    {
        Assert.Null(PackageGrading.Pick(PackageGradingMethods.Highest, []));
    }

    [Fact]
    public void Highest_keeps_the_best_score_and_a_later_lower_attempt_never_lowers_it()
    {
        var best = A(0, 80);
        var later = A(10, 60);

        Assert.Same(best, PackageGrading.Pick(PackageGradingMethods.Highest, [best, later]));
        Assert.Same(best, PackageGrading.Pick(PackageGradingMethods.Highest, [later, best]));
    }

    [Fact]
    public void Highest_compares_shares_of_the_maximum_and_prefers_the_earlier_attempt_on_a_tie()
    {
        var eightyOfHundred = A(0, 80, 100);
        var fourOfFive = A(5, 4, 5); // 80 % as well
        var nineOfTen = A(10, 9, 10);

        Assert.Same(eightyOfHundred, PackageGrading.Pick(PackageGradingMethods.Highest, [eightyOfHundred, fourOfFive]));
        Assert.Same(nineOfTen, PackageGrading.Pick(PackageGradingMethods.Highest, [eightyOfHundred, fourOfFive, nineOfTen]));
    }

    [Fact]
    public void Highest_ignores_attempts_without_a_score_unless_none_has_one()
    {
        var scored = A(0, 50);
        var unscored = A(10, null);

        Assert.Same(scored, PackageGrading.Pick(PackageGradingMethods.Highest, [scored, unscored]));
        Assert.Same(unscored, PackageGrading.Pick(PackageGradingMethods.Highest, [A(0, null), unscored]));
    }

    [Fact]
    public void Last_takes_the_newest_and_First_the_oldest_completed_attempt()
    {
        var first = A(0, 90);
        var middle = A(10, 40);
        var last = A(20, 60);

        Assert.Same(last, PackageGrading.Pick(PackageGradingMethods.Last, [middle, last, first]));
        Assert.Same(first, PackageGrading.Pick(PackageGradingMethods.First, [middle, last, first]));
    }

    [Fact]
    public void Domain_method_values_equal_the_contract_values()
    {
        Assert.Equal(PackageGradingMethods.Highest, PackageUpload.DefaultGradingMethod);
    }
}
