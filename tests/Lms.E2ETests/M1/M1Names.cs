namespace Lms.E2ETests.M1;

/// <summary>
/// Unique business names for one test, so reruns against the same database never collide (codes and admin emails
/// are unique). Arabic names carry the same suffix, so a search finds the row in both languages.
/// </summary>
public sealed record M1Names(string Suffix)
{
    public static M1Names New() => new(Guid.NewGuid().ToString("N")[..8]);

    public string SchoolGroupAr => $"مدارس التجمع {Suffix}";
    public string SchoolGroupEn => $"Altagamo3 Schools {Suffix}";
    public string UniversityAr => $"جامعة MNF {Suffix}";
    public string UniversityEn => $"MNF University {Suffix}";

    public string RootCode(string prefix) => $"{prefix}-{Suffix}".ToUpperInvariant();

    public string AdminEmail(string role) => $"e2e-{role}-{Suffix}@lms.test";

    /// <summary>A password that satisfies the policy (8+, upper, lower, digit) and is unique per run.</summary>
    public string NewPassword => $"E2e-Pass-{Suffix}1A";
}
