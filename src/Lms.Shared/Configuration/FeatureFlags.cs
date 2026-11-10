namespace Lms.Shared.Configuration;

/// <summary>
/// Feature flags (PRD §4.4): boolean inheritable settings, off by default, switched on per client or subtree.
/// Append only; never rename a Name.
/// </summary>
public static class FeatureFlags
{
    public static readonly SettingKey<bool> VirtualClasses = new("Features.VirtualClasses", false);
    public static readonly SettingKey<bool> DigitalLibrary = new("Features.DigitalLibrary", false);
    public static readonly SettingKey<bool> ScormContent = new("Features.ScormContent", false);
    public static readonly SettingKey<bool> Messaging = new("Features.Messaging", false);
    public static readonly SettingKey<bool> GuardianPortal = new("Features.GuardianPortal", false);

    public static IReadOnlyList<SettingKey<bool>> All { get; } =
        [VirtualClasses, DigitalLibrary, ScormContent, Messaging, GuardianPortal];
}
