namespace Lms.Domain.Grading.Calculation;

/// <summary>Rounding of the final percentage: half-up (0.5 goes up), once, at the end.</summary>
public static class Rounding
{
    public static decimal HalfUp(decimal value, int decimalPlaces) =>
        Math.Round(value, Math.Clamp(decimalPlaces, 0, 6), MidpointRounding.AwayFromZero);
}
