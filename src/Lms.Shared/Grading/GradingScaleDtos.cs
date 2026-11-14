namespace Lms.Shared.Grading;

/// <summary>
/// One band of a scale. Bands cover 0-100 % without gaps or overlaps (<c>scale_bands_invalid</c> names the gaps). <c>Points</c> is the GPA
/// value of a <c>Gpa</c> scale (D58: GPA = the band's points per course). Descriptors are the Ministry-style Arabic and English texts (D59).
/// </summary>
public sealed record GradingScaleBandDto(
    Guid Id,
    int Order,
    decimal MinPercent,
    decimal MaxPercent,
    string Code,
    string? LabelAr,
    string? LabelEn,
    string? DescriptorAr,
    string? DescriptorEn,
    decimal? Points);

/// <summary>
/// A grading scale of an org unit. Units see the scales of their ancestors (read only, <c>InheritedFromUnitId</c> set). Provisioning creates one
/// plain default "Percentage" scale and the Saudi presets as <c>IsTemplate</c> scales (never the default, L7); every seeded scale is
/// <c>Provisional</c> until an admin confirms or edits it, and publishing under a provisional scale needs an acknowledgement.
/// <c>UpdateAvailable</c> = the seed moved on (<c>SeedVersion</c>) and this unedited copy can be updated. <c>InUse</c> = a gradebook uses it
/// (<c>scale_in_use</c> on delete).
/// </summary>
public sealed record GradingScaleDto(
    Guid Id,
    Guid OrgUnitId,
    string NameAr,
    string NameEn,
    string Kind,
    bool IsDefault,
    bool IsTemplate,
    bool Provisional,
    string? SeedKey,
    int? SeedVersion,
    bool UpdateAvailable,
    Guid? InheritedFromUnitId,
    bool InUse,
    IReadOnlyList<GradingScaleBandDto> Bands);

/// <summary>One band of a save request (no id: the band list is replaced as a whole).</summary>
public sealed record SaveGradingScaleBandRequest(
    int Order,
    decimal MinPercent,
    decimal MaxPercent,
    string Code,
    string? LabelAr,
    string? LabelEn,
    string? DescriptorAr,
    string? DescriptorEn,
    decimal? Points);

/// <summary>
/// POST/PUT <c>/api/org-units/{id}/grading-scales[/{sid}]</c> (<c>Grades.ConfigureScales</c>, <c>UnitAccess</c>). The band list replaces the
/// stored one; editing a band of a provisional copy clears its <c>Provisional</c> flag. 400 <c>scale_bands_invalid</c> when the bands leave a
/// gap or overlap or are more than <see cref="GradingLimits.MaxBands"/>.
/// </summary>
public sealed record SaveGradingScaleRequest(
    string NameAr,
    string NameEn,
    string Kind,
    IReadOnlyList<SaveGradingScaleBandRequest> Bands);

/// <summary>
/// POST <c>…/grading-scales/presets</c> (<c>Grades.ConfigureScales</c>): makes a copy of a preset template (<c>SeedKey</c>) at the unit; the
/// copy stays <c>Provisional</c> until confirmed or edited (L7).
/// </summary>
public sealed record CopyScalePresetRequest(string SeedKey);

/// <summary>One item of a weight template: a category name and its weight (0-100).</summary>
public sealed record WeightTemplateItemDto(Guid? Id, string NameAr, string NameEn, decimal Weight, int Order);

/// <summary>
/// A weight template of an org unit (the categories and weights a teacher applies to a gradebook; applying copies the values). The weights
/// should sum to 100 (<c>WeightsTotal</c>: a warning, not a refusal, like categories).
/// </summary>
public sealed record WeightTemplateDto(
    Guid Id,
    Guid OrgUnitId,
    string NameAr,
    string NameEn,
    decimal WeightsTotal,
    Guid? InheritedFromUnitId,
    IReadOnlyList<WeightTemplateItemDto> Items);

/// <summary>POST/PUT <c>/api/org-units/{id}/weight-templates[/{tid}]</c> (<c>Grades.ConfigureScales</c>). 400 <c>weights_invalid</c> for a weight outside 0-100.</summary>
public sealed record SaveWeightTemplateRequest(string NameAr, string NameEn, IReadOnlyList<WeightTemplateItemDto> Items);
