namespace Lms.Shared.Configuration;

/// <summary>Typed key of an inheritable setting (PRD §4.3) with its default value.</summary>
public sealed record SettingKey<T>(string Name, T Default);

/// <summary>Settings catalog. Later milestones append keys; never rename a Name. Every key is listed in <see cref="SettingCatalog"/>.</summary>
public static class SettingKeys
{
    public static readonly SettingKey<string> UiDefaultCulture = new("Ui.DefaultCulture", "ar");
    public static readonly SettingKey<int> FilesMaxUploadMb = new("Files.MaxUploadMb", 200);
    public static readonly SettingKey<bool> UiShowHijriDates = new("Ui.ShowHijriDates", false);

    /// <summary>Days before a staff member's <c>ExpiresOn</c> that admins get a notice (D35); 0 = no pre-expiry notice.</summary>
    public static readonly SettingKey<int> StaffExpiryNoticeDays = new("Staff.ExpiryNoticeDays", 14);
}
