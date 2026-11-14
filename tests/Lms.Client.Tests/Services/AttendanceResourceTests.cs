using System.Globalization;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Excuses;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

/// <summary>M5 wave 3: every attendance and excuse code, event, setting, permission and catalog value has a label in both languages.</summary>
public sealed class AttendanceResourceTests : ClientTestContext
{
    private static readonly string[] Codes =
    [
        ProblemCodes.attendance_edit_window_closed, ProblemCodes.attendance_session_future, ProblemCodes.attendance_unmarked_rows,
        ProblemCodes.attendance_reason_required, ProblemCodes.attendance_not_a_proposal, ProblemCodes.excuse_out_of_policy,
        ProblemCodes.excuse_submitter_not_allowed, ProblemCodes.excuse_reviewer_is_submitter, ProblemCodes.excuse_comment_required,
        ProblemCodes.excuse_attachment_invalid, ProblemCodes.excuse_not_pending,
    ];

    private static readonly string[] SettingNames =
    [
        SettingKeys.AttendanceGenerateDaysAhead.Name, SettingKeys.AttendanceEditWindowHours.Name, SettingKeys.AttendanceLiveMinPercent.Name,
        SettingKeys.AttendanceLateAfterMinutes.Name, SettingKeys.AttendanceAbsentThreshold.Name, SettingKeys.AttendanceThresholdWindowDays.Name,
        SettingKeys.AttendanceLateWeightPercent.Name, SettingKeys.ExcusesSubmitters.Name, SettingKeys.ExcusesMaxDaysAfterAbsence.Name,
        SettingKeys.ExcusesAllowedReasons.Name, SettingKeys.ExcusesAttachmentRequiredReasons.Name, SettingKeys.ExcusesMaxAttachmentMb.Name,
        SettingKeys.ExcusesAttachmentRetentionDays.Name,
    ];

    private static readonly string[] Events =
    [
        NotificationEvents.AttendanceAbsent, NotificationEvents.ExcusesDecided, NotificationEvents.ExcusesEscalated, NotificationEvents.AttendanceThresholdCrossed,
    ];

    private static IEnumerable<string> Keys()
    {
        foreach (var code in Codes)
        {
            yield return "Error_" + code;
        }

        foreach (var name in SettingNames)
        {
            yield return "Setting_" + name.Replace('.', '_');
        }

        foreach (var value in ExcuseSubmitters.All)
        {
            yield return "SettingValue_Excuses_Submitters_" + value;
        }

        foreach (var value in ExcuseReasons.All)
        {
            yield return "SettingValue_Excuses_AllowedReasons_" + value;
            yield return "SettingValue_Excuses_AttachmentRequiredReasons_" + value;
            yield return "Exc_Reason_" + value;
        }

        foreach (var e in Events)
        {
            yield return "NotifEvent_" + e.Replace('.', '_');
            yield return "Notif_" + e.Replace('.', '_');
        }

        foreach (var v in AttendanceStatuses.All)
        {
            yield return "Att_Status_" + v;
        }

        foreach (var v in AttendanceSources.All)
        {
            yield return "Att_Source_" + v;
        }

        foreach (var v in AttendanceSessionStates.All)
        {
            yield return "Att_State_" + v;
        }

        foreach (var v in AttendanceChangeKinds.All)
        {
            yield return "Att_ChangeKind_" + v;
        }

        foreach (var v in ExcuseStatuses.All)
        {
            yield return "Exc_Status_" + v;
        }

        foreach (var v in ExcusePolicyViolations.All)
        {
            yield return "Exc_Violation_" + v;
        }

        foreach (var p in Permissions.All.Where(p => p.StartsWith("Attendance.", StringComparison.Ordinal) || p.StartsWith("Excuses.", StringComparison.Ordinal)))
        {
            yield return "Perm_" + p.Replace('.', '_');
        }

        yield return "PermModule_Attendance";
        yield return "PermModule_Excuses";
        yield return "AuditEntity_AttendanceRecordChange";
        yield return "AuditEntity_AbsenceExcuse";
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_attendance_and_excuse_label_exists(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        var missing = Keys().Where(k => l[k].ResourceNotFound).ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Notification_texts_use_only_the_parameters_of_their_event(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        foreach (var key in Events)
        {
            var text = l["Notif_" + key.Replace('.', '_')].Value;
            var parameters = NotificationEvents.Find(key)!.Parameters.Select(p => p.EndsWith("Ar", StringComparison.Ordinal) || p.EndsWith("En", StringComparison.Ordinal) ? p[..^2] : p).ToHashSet();
            var placeholders = System.Text.RegularExpressions.Regex.Matches(text, @"\{(\w+)\}").Select(m => m.Groups[1].Value).ToList();

            Assert.NotEmpty(placeholders);
            Assert.All(placeholders, p => Assert.Contains(p, parameters));
        }
    }
}
