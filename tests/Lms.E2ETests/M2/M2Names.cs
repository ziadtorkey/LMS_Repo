namespace Lms.E2ETests.M2;

/// <summary>
/// Unique people for one M2 test, so reruns against the same database never collide (a staff email, mobile and
/// national id are unique per client, D26). Arabic names carry the same suffix so a search finds the row in both languages.
/// </summary>
public sealed record M2Names(string Suffix)
{
    public static M2Names New() => new(Guid.NewGuid().ToString("N")[..8]);

    public string NameAr(string label) => $"{label} {Suffix}";

    public string NameEn(string label) => $"{label} {Suffix}";

    public string Email(string label) => $"e2e-{label}-{Suffix}@lms.test";

    /// <summary>A mobile number that matches <c>^\+?[0-9]{8,15}$</c>, unique per run.</summary>
    public string Mobile => "+9665" + Convert.ToUInt32(Suffix[..7], 16).ToString("D9")[..8];

    /// <summary>A password that satisfies the policy (8+, upper, lower, digit) and is unique per run.</summary>
    public string NewPassword => $"E2e-Pass-{Suffix}1A";
}
