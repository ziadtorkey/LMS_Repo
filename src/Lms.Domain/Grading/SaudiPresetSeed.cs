using Lms.Domain.Organizations;

namespace Lms.Domain.Grading;

/// <summary>One band of a seeded scale. Percentages follow <see cref="ScaleBandValidator"/>: minimum inclusive, maximum exclusive (100 in the last).</summary>
public sealed record SeedBand(
    decimal Min, decimal Max, string Code, string LabelAr, string LabelEn, string DescriptorAr, string DescriptorEn, decimal? Points);

/// <summary>A preset scale of the seed. <see cref="Version"/> grows when its numbers or texts change (L7).</summary>
public sealed record SeedScale(
    string Key, int Version, string Kind, string NameAr, string NameEn, OrganizationType? DefaultSuggestionFor,
    IReadOnlyList<SeedBand> Bands);

/// <summary>The Saudi presets of D59 as data, and the one place their numbers live (the engine depends on none of them).</summary>
public static class SaudiPresetSeed
{
    public const string SchoolKey = "sa-school-5band";
    public const string University5Key = "sa-university-5point";
    public const string University4Key = "sa-university-4point";

    /// <summary>The key of the plain default scale every client gets: percentages without band text.</summary>
    public const string PlainDefaultKey = "default-percentage";

    public static IReadOnlyList<SeedScale> All => [School, University5, University4];

    // NeedsVerification (Q21): the ministry's official school bands and descriptors were not available to the team. These five bands are the
    // commonly published ones, provisional until the owner or the ministry document confirms them; early primary grades may use descriptors only.
    private static readonly SeedScale School = new(
        SchoolKey, 1, "Letter", "سلم تقدير المدارس (سعودي)", "School bands (Saudi)", null,
        [
            new(90, 100m, "A", "ممتاز", "Excellent", "أداء متميز يفوق المتوقع", "Outstanding performance above expectations", null),
            new(80, 90, "B", "جيد جدًا", "Very Good", "أداء جيد جدًا يحقق المتوقع", "Very good performance that meets expectations", null),
            new(70, 80, "C", "جيد", "Good", "أداء جيد", "Good performance", null),
            new(60, 70, "D", "مقبول", "Acceptable", "أداء مقبول في الحد الأدنى", "Acceptable, at the minimum level", null),
            new(0, 60, "F", "راسب", "Fail", "أداء دون الحد الأدنى", "Below the minimum level", null),
        ]);

    // NeedsVerification (Q23): the 5.00-point table is the common pattern of Saudi public universities, not a citation; each university sets its own.
    private static readonly SeedScale University5 = new(
        University5Key, 1, "Gpa", "المعدل التراكمي من 5 (جامعات سعودية)", "GPA out of 5 (Saudi universities)", OrganizationType.University,
        UniversityBands(5.00m, 4.75m, 4.50m, 4.00m, 3.50m, 3.00m, 2.50m, 2.00m, 1.00m));

    // NeedsVerification (Q23): the 4.00-point alternative with the same ranges.
    private static readonly SeedScale University4 = new(
        University4Key, 1, "Gpa", "المعدل التراكمي من 4 (جامعات سعودية)", "GPA out of 4 (Saudi universities)", null,
        UniversityBands(4.00m, 3.75m, 3.50m, 3.00m, 2.50m, 2.00m, 1.50m, 1.00m, 0.00m));

    public static SeedScale? Find(string key) => All.FirstOrDefault(s => s.Key == key);

    private static IReadOnlyList<SeedBand> UniversityBands(
        decimal aPlus, decimal a, decimal bPlus, decimal b, decimal cPlus, decimal c, decimal dPlus, decimal d, decimal f) =>
    [
        new(95, 100m, "A+", "ممتاز مرتفع", "Exceptional", "أداء استثنائي", "Exceptional performance", aPlus),
        new(90, 95, "A", "ممتاز", "Excellent", "أداء ممتاز", "Excellent performance", a),
        new(85, 90, "B+", "جيد جدًا مرتفع", "Very Good Plus", "أداء جيد جدًا مرتفع", "Higher very good performance", bPlus),
        new(80, 85, "B", "جيد جدًا", "Very Good", "أداء جيد جدًا", "Very good performance", b),
        new(75, 80, "C+", "جيد مرتفع", "Good Plus", "أداء جيد مرتفع", "Higher good performance", cPlus),
        new(70, 75, "C", "جيد", "Good", "أداء جيد", "Good performance", c),
        new(65, 70, "D+", "مقبول مرتفع", "Pass Plus", "أداء مقبول مرتفع", "Higher acceptable performance", dPlus),
        new(60, 65, "D", "مقبول", "Pass", "أداء مقبول", "Acceptable performance", d),
        new(0, 60, "F", "راسب", "Fail", "أداء دون الحد الأدنى", "Below the minimum level", f),
    ];
}
