using System.Globalization;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;

namespace Lms.Infrastructure.Comms.Preferences;

/// <summary>
/// The validation of a saved communication-settings document (M6 plan Task 9, L8, L24): a daily cap above its platform ceiling, a per-recipient or messaging
/// value outside its catalog range, or a malformed quiet-hours time is refused with a code and the field name. The ceilings are the maximum of the settings
/// catalog, so there is one source for the DTO, the validation and the settings panel.
/// </summary>
public static class CommsSettingsRules
{
    public sealed record Violation(string Field, string Code);

    public static CommsCeilingsDto Ceilings { get; } = new(
        SettingCatalog.All.Single(d => d.Name == SettingKeys.CommsMaxEmailPerDay.Name).Max!.Value,
        SettingCatalog.All.Single(d => d.Name == SettingKeys.CommsMaxPushPerDay.Name).Max!.Value);

    public static IReadOnlyList<Violation> Validate(CommsSettingsDto s)
    {
        var violations = new List<Violation>();
        Cap(violations, nameof(s.MaxEmailPerDay), s.MaxEmailPerDay, SettingKeys.CommsMaxEmailPerDay.Name, aboveCeilingCode: ProblemCodes.comms_cap_above_ceiling);
        Cap(violations, nameof(s.MaxPushPerDay), s.MaxPushPerDay, SettingKeys.CommsMaxPushPerDay.Name, aboveCeilingCode: ProblemCodes.comms_cap_above_ceiling);
        Range(violations, nameof(s.MaxEmailPerUserPerDay), s.MaxEmailPerUserPerDay, SettingKeys.CommsMaxEmailPerUserPerDay.Name);
        Range(violations, nameof(s.MaxPushPerUserPerDay), s.MaxPushPerUserPerDay, SettingKeys.CommsMaxPushPerUserPerDay.Name);
        Range(violations, nameof(s.DeliveryRetentionDays), s.DeliveryRetentionDays, SettingKeys.CommsDeliveryRetentionDays.Name);
        if (!IsTime(s.QuietHoursStart))
        {
            violations.Add(new Violation(nameof(s.QuietHoursStart), ProblemCodes.validation_failed));
        }

        if (!IsTime(s.QuietHoursEnd))
        {
            violations.Add(new Violation(nameof(s.QuietHoursEnd), ProblemCodes.validation_failed));
        }

        return violations;
    }

    /// <summary>A time of day as <c>HH:mm</c> (24 hours).</summary>
    public static bool IsTime(string? value) =>
        value is { Length: 5 } && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static void Cap(List<Violation> into, string field, int value, string setting, string aboveCeilingCode)
    {
        var d = SettingCatalog.All.Single(x => x.Name == setting);
        if (value > d.Max)
        {
            into.Add(new Violation(field, aboveCeilingCode));
        }
        else if (value < d.Min)
        {
            into.Add(new Violation(field, ProblemCodes.validation_failed));
        }
    }

    private static void Range(List<Violation> into, string field, int value, string setting)
    {
        var d = SettingCatalog.All.Single(x => x.Name == setting);
        if (value < d.Min || value > d.Max)
        {
            into.Add(new Violation(field, ProblemCodes.validation_failed));
        }
    }
}
