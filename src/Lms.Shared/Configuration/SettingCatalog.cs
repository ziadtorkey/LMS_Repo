namespace Lms.Shared.Configuration;

/// <summary>JSON value kind of a setting.</summary>
public static class SettingValueKinds
{
    public const string Bool = "Bool";
    public const string Int = "Int";
    public const string String = "String";
    public const string Enum = "Enum";
}

/// <summary>UI grouping of settings.</summary>
public static class SettingCategories
{
    public const string General = "General";
    public const string Files = "Files";
    public const string Features = "Features";
    public const string People = "People";
}

/// <summary>
/// Describes one editable setting. <c>DefaultJson</c> is the JSON form of the key's default
/// (e.g. <c>"ar"</c> with quotes, <c>false</c>, <c>200</c>).
/// </summary>
public sealed record SettingDefinition(
    string Name,
    string Kind,
    string Category,
    string DefaultJson,
    IReadOnlyList<string>? AllowedValues = null,
    int? Min = null,
    int? Max = null);

/// <summary>Every setting the settings panel can show and edit (FR-ORG-11, §4.3, §4.4).</summary>
public static class SettingCatalog
{
    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        new(SettingKeys.UiDefaultCulture.Name, SettingValueKinds.Enum, SettingCategories.General, "\"ar\"",
            AllowedValues: ["ar", "en"]),
        new(SettingKeys.UiShowHijriDates.Name, SettingValueKinds.Bool, SettingCategories.General, "false"),
        new(SettingKeys.FilesMaxUploadMb.Name, SettingValueKinds.Int, SettingCategories.Files, "200", Min: 1, Max: 2048),
        new(FeatureFlags.VirtualClasses.Name, SettingValueKinds.Bool, SettingCategories.Features, "false"),
        new(FeatureFlags.DigitalLibrary.Name, SettingValueKinds.Bool, SettingCategories.Features, "false"),
        new(FeatureFlags.ScormContent.Name, SettingValueKinds.Bool, SettingCategories.Features, "false"),
        new(FeatureFlags.Messaging.Name, SettingValueKinds.Bool, SettingCategories.Features, "false"),
        new(FeatureFlags.GuardianPortal.Name, SettingValueKinds.Bool, SettingCategories.Features, "false"),
        new(SettingKeys.StaffExpiryNoticeDays.Name, SettingValueKinds.Int, SettingCategories.People, "14", Min: 0, Max: 90),
    ];

    /// <summary>The definition with this exact (case-sensitive) name, or null.</summary>
    public static SettingDefinition? Find(string name) =>
        All.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
}
