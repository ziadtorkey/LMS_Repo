namespace Lms.Shared.Configuration;

/// <summary>Typed key of an inheritable setting (PRD §4.3) with its default value.</summary>
public sealed record SettingKey<T>(string Name, T Default);

/// <summary>Settings catalog. Later milestones append keys; never rename a Name.</summary>
public static class SettingKeys
{
    public static readonly SettingKey<string> UiDefaultCulture = new("Ui.DefaultCulture", "ar");
    public static readonly SettingKey<int> FilesMaxUploadMb = new("Files.MaxUploadMb", 200);
}
