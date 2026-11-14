using Lms.Domain.Attendance;

namespace Lms.UnitTests.Attendance;

public sealed class ThresholdCalculatorTests
{
    [Theory]
    [InlineData(5, 0, 50, 5.0)]
    [InlineData(4, 1, 50, 4.5)]
    [InlineData(4, 2, 50, 5.0)]
    [InlineData(0, 10, 50, 5.0)]
    [InlineData(3, 4, 25, 4.0)]
    [InlineData(2, 6, 0, 2.0)]
    public void A_late_counts_at_its_configured_share(int absences, int lates, int percent, double expected) =>
        Assert.Equal((decimal)expected, ThresholdCalculator.Score(absences, lates, percent));

    [Theory]
    [InlineData(4.5, 5, false)]
    [InlineData(5.0, 5, true)]
    [InlineData(7.5, 5, true)]
    [InlineData(0, 1, false)]
    public void The_threshold_is_reached_not_exceeded(double score, int threshold, bool crossed) =>
        Assert.Equal(crossed, ThresholdCalculator.Crossed((decimal)score, threshold));
}
