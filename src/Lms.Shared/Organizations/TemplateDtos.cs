namespace Lms.Shared.Organizations;

/// <summary>Who can see an org template: its owner unit's admins only, or the whole client.</summary>
public static class TemplateVisibilities
{
    public const string Private = "Private";
    public const string Shared = "Shared";

    public static IReadOnlyList<string> All { get; } = [Private, Shared];
}

/// <summary>
/// One unit of a template. <c>Key</c>/<c>ParentKey</c>/<c>NextTransferKey</c> are template-local ids;
/// names and code may hold placeholders like <c>{Year}</c>.
/// </summary>
public sealed record TemplateNodeDto(
    int Key,
    int? ParentKey,
    Guid LevelTypeId,
    string NameAr,
    string NameEn,
    string Code,
    bool IsLevelTransfer,
    int? NextTransferKey,
    bool IsCourseManagementLevel,
    IReadOnlyList<string> PolicyKeys,
    int SettingCount);

/// <summary>List item of GET /api/templates; <c>Placeholders</c> = placeholder names without braces.</summary>
public sealed record OrgTemplateSummaryDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Visibility,
    Guid OwnerOrgUnitId,
    string OwnerNameAr,
    string OwnerNameEn,
    int UnitCount,
    IReadOnlyList<string> Placeholders,
    DateTime CreatedAt);

/// <summary>Response of GET /api/templates/{id}.</summary>
public sealed record OrgTemplateDetailDto(OrgTemplateSummaryDto Summary, IReadOnlyList<TemplateNodeDto> Nodes);

/// <summary>
/// Body of POST /api/templates: saves the branch under <c>SourceOrgUnitId</c>. <c>Parameterize</c> maps literal
/// text to a placeholder, e.g. <c>{"2026": "{Year}"}</c>.
/// </summary>
public sealed record SaveTemplateRequest(
    Guid SourceOrgUnitId,
    string NameAr,
    string NameEn,
    string Visibility,
    bool IncludePolicies,
    bool IncludeSettings,
    IReadOnlyDictionary<string, string> Parameterize);

/// <summary>Body of POST /api/templates/{id}/apply: placeholder name (no braces) to value.</summary>
public sealed record ApplyTemplateRequest(Guid TargetParentId, IReadOnlyDictionary<string, string> Placeholders);

/// <summary>Result of applying a template.</summary>
public sealed record ApplyTemplateResultDto(Guid NewRootId, int UnitsCreated, IReadOnlyList<string> SkippedLockedSettings);
