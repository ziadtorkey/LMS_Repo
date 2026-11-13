namespace Lms.E2ETests.M3;

/// <summary>
/// Unique names for one M3 test. Course and class codes are unique client-wide (D38), so every code carries the
/// suffix; Arabic names carry it too, so a search finds the row in both languages.
/// </summary>
public sealed record M3Names(string Suffix)
{
    public static M3Names New() => new(Guid.NewGuid().ToString("N")[..8]);

    public string CourseCode(string label) => $"{label}-{Suffix}".ToUpperInvariant();

    public string CourseNameAr(string label) => $"{label} {Suffix}";

    public string CourseNameEn(string label) => $"{label} {Suffix}";

    public string Email(string label) => $"e2e-{label}-{Suffix}@lms.test";

    /// <summary>A password that satisfies the policy (8+, upper, lower, digit) and is unique per run.</summary>
    public string NewPassword => $"E2e-Pass-{Suffix}1A";
}
