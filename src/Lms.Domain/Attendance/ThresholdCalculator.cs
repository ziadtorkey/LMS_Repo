namespace Lms.Domain.Attendance;

/// <summary>The absence score of D61: absences plus lates at a configurable share (default one half). An Excused record never counts.</summary>
public static class ThresholdCalculator
{
    public static decimal Score(int absences, int lates, int lateWeightPercent) => absences + lates * lateWeightPercent / 100m;

    /// <summary>True when the score reaches the threshold (5 absences in 30 days by default).</summary>
    public static bool Crossed(decimal score, int threshold) => score >= threshold;
}
