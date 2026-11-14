using Lms.Shared.People;

namespace Lms.Shared.Configuration;

/// <summary>JSON value kind of a setting.</summary>
public static class SettingValueKinds
{
    public const string Bool = "Bool";
    public const string Int = "Int";
    public const string String = "String";
    public const string Enum = "Enum";

    /// <summary>A JSON array of distinct strings, each one of <c>AllowedValues</c> (M3).</summary>
    public const string StringSet = "StringSet";
}

/// <summary>UI grouping of settings.</summary>
public static class SettingCategories
{
    public const string General = "General";
    public const string Files = "Files";
    public const string Features = "Features";
    public const string People = "People";
    public const string Academic = "Academic";
}

/// <summary>
/// Describes one editable setting. <c>DefaultJson</c> is the JSON form of the key's default
/// (e.g. <c>"ar"</c> with quotes, <c>false</c>, <c>200</c>). <c>ClientLevelOnly</c> settings are read and written only at the client root and are
/// never overridden by a unit (M5: <c>Org.TimeZone</c>, <c>AcademicYears.TermsPerYear</c>); the settings endpoint enforces it.
/// </summary>
public sealed record SettingDefinition(
    string Name,
    string Kind,
    string Category,
    string DefaultJson,
    IReadOnlyList<string>? AllowedValues = null,
    int? Min = null,
    int? Max = null,
    bool ClientLevelOnly = false);

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
        new(SettingKeys.StudentsCodeMode.Name, SettingValueKinds.Enum, SettingCategories.People, "\"Auto\"",
            AllowedValues: StudentCodeModes.All),
        new(SettingKeys.StudentsCodePattern.Name, SettingValueKinds.String, SettingCategories.People, "\"{Seq:6}\""),
        new(SettingKeys.StudentsCriticalFields.Name, SettingValueKinds.StringSet, SettingCategories.People,
            "[\"NameAr\",\"NameEn\",\"DateOfBirth\",\"Gender\",\"Religion\",\"Nationality\",\"NationalId\"]",
            AllowedValues: StudentFields.Requestable),
        new(SettingKeys.GuardiansDataRequired.Name, SettingValueKinds.Bool, SettingCategories.People, "false"),
        new(SettingKeys.ClassesCapacityMode.Name, SettingValueKinds.Enum, SettingCategories.Academic, "\"Warn\"",
            AllowedValues: ["Warn", "Block"]),
        new(SettingKeys.BlueprintsSectionLabel.Name, SettingValueKinds.Enum, SettingCategories.Academic, "\"Week\"",
            AllowedValues: ["Week", "Unit", "Chapter"]),
        new(SettingKeys.EnrollmentTransferCarriesGrades.Name, SettingValueKinds.Bool, SettingCategories.Academic, "true"),
        new(SettingKeys.AssignmentsLatePenaltyDefaultPercent.Name, SettingValueKinds.Int, SettingCategories.Academic, "10",
            Min: 0, Max: 100),
        new(SettingKeys.QuizzesAutoSubmitGraceSeconds.Name, SettingValueKinds.Int, SettingCategories.Academic, "30",
            Min: 0, Max: 300),
        new(SettingKeys.PackagesMaxEntries.Name, SettingValueKinds.Int, SettingCategories.Files, "10000", Min: 100, Max: 100000),
        new(SettingKeys.PackagesMaxUncompressedMb.Name, SettingValueKinds.Int, SettingCategories.Files, "1024", Min: 1, Max: 10240),
        new(SettingKeys.PackagesMaxClientStorageMb.Name, SettingValueKinds.Int, SettingCategories.Files, "10240", Min: 1, Max: 1048576),
        new(SettingKeys.PackagesTokenMinutes.Name, SettingValueKinds.Int, SettingCategories.Files, "120", Min: 5, Max: 480),
        new(SettingKeys.PackagesCommitMinIntervalSeconds.Name, SettingValueKinds.Int, SettingCategories.Files, "2", Min: 0, Max: 60),
        // M4b live sessions: Academic, like the other teaching settings (the settings page shows a fixed category list).
        new(SettingKeys.LiveJoinWindowMinutes.Name, SettingValueKinds.Int, SettingCategories.Academic, "15", Min: 0, Max: 60),
        new(SettingKeys.LiveReminderMinutes.Name, SettingValueKinds.Int, SettingCategories.Academic, "30", Min: 5, Max: 1440),
        new(SettingKeys.LiveMaxCreatesPerDay.Name, SettingValueKinds.Int, SettingCategories.Academic, "500",
            Min: 1, Max: 10_000),
        new(SettingKeys.LiveCloudRecordingEnabled.Name, SettingValueKinds.Bool, SettingCategories.Academic, "false"),
        // M5 wave 1: the client time zone (a free IANA id, validated by the server), grading and term structure.
        new(SettingKeys.OrgTimeZone.Name, SettingValueKinds.String, SettingCategories.General, "\"Asia/Riyadh\"", ClientLevelOnly: true),
        new(SettingKeys.GradesMissingPolicy.Name, SettingValueKinds.Enum, SettingCategories.Academic, "\"Normalise\"",
            AllowedValues: ["Normalise", "Zero", "HideUntilComplete"]),
        new(SettingKeys.GradesCategoryMethod.Name, SettingValueKinds.Enum, SettingCategories.Academic, "\"PointsWeighted\"",
            AllowedValues: ["PointsWeighted", "PercentAverage"]),
        new(SettingKeys.GradesDecimalPlaces.Name, SettingValueKinds.Int, SettingCategories.Academic, "2", Min: 0, Max: 4),
        new(SettingKeys.AcademicYearsTermsPerYear.Name, SettingValueKinds.Int, SettingCategories.Academic, "2", Min: 2, Max: 3, ClientLevelOnly: true),
        // M6 wave 1: communication settings (client level, Org Admin at the client root; D66 defaults).
        new(SettingKeys.CommsEmailEnabled.Name, SettingValueKinds.Bool, SettingCategories.Features, "true", ClientLevelOnly: true),
        new(SettingKeys.CommsPushEnabled.Name, SettingValueKinds.Bool, SettingCategories.Features, "false", ClientLevelOnly: true),
        new(SettingKeys.CommsMaxEmailPerDay.Name, SettingValueKinds.Int, SettingCategories.Features, "5000", Min: 0, Max: 100_000, ClientLevelOnly: true),
        new(SettingKeys.CommsMaxPushPerDay.Name, SettingValueKinds.Int, SettingCategories.Features, "20000", Min: 0, Max: 200_000, ClientLevelOnly: true),
        new(SettingKeys.CommsMaxEmailPerUserPerDay.Name, SettingValueKinds.Int, SettingCategories.Features, "20", Min: 1, Max: 200, ClientLevelOnly: true),
        new(SettingKeys.CommsMaxPushPerUserPerDay.Name, SettingValueKinds.Int, SettingCategories.Features, "50", Min: 1, Max: 500, ClientLevelOnly: true),
        new(SettingKeys.CommsQuietHoursEnabled.Name, SettingValueKinds.Bool, SettingCategories.Features, "false", ClientLevelOnly: true),
        new(SettingKeys.CommsQuietHoursStart.Name, SettingValueKinds.String, SettingCategories.Features, "\"21:00\"", ClientLevelOnly: true),
        new(SettingKeys.CommsQuietHoursEnd.Name, SettingValueKinds.String, SettingCategories.Features, "\"07:00\"", ClientLevelOnly: true),
        new(SettingKeys.CommsDeliveryRetentionDays.Name, SettingValueKinds.Int, SettingCategories.Features, "90", Min: 7, Max: 730, ClientLevelOnly: true),
        new(SettingKeys.MessagingReadReceipts.Name, SettingValueKinds.Bool, SettingCategories.Features, "false", ClientLevelOnly: true),
        new(SettingKeys.MessagingMaxPerMinute.Name, SettingValueKinds.Int, SettingCategories.Features, "30", Min: 1, Max: 120, ClientLevelOnly: true),
        new(SettingKeys.MessagingMaxNewConversationsPerDay.Name, SettingValueKinds.Int, SettingCategories.Features, "20", Min: 1, Max: 200, ClientLevelOnly: true),
        new(SettingKeys.MessagingMaxGroupSize.Name, SettingValueKinds.Int, SettingCategories.Features, "50", Min: 3, Max: 200, ClientLevelOnly: true),
        new(SettingKeys.ModerationReportContextRetentionDays.Name, SettingValueKinds.Int, SettingCategories.Features, "180", Min: 30, Max: 730, ClientLevelOnly: true),
        new(SettingKeys.ModerationMaxReportsPerUserPerDay.Name, SettingValueKinds.Int, SettingCategories.Features, "20", Min: 1, Max: 200, ClientLevelOnly: true),
        new(SettingKeys.ModerationSafetyReviewHours.Name, SettingValueKinds.Int, SettingCategories.Features, "24", Min: 1, Max: 168, ClientLevelOnly: true),
        new(SettingKeys.MessagingUploadsPerMinute.Name, SettingValueKinds.Int, SettingCategories.Features, "10", Min: 1, Max: 60, ClientLevelOnly: true),
        new(SettingKeys.MessagingRetentionYearsAfterClose.Name, SettingValueKinds.Int, SettingCategories.Features, "1", Min: 1, Max: 10, ClientLevelOnly: true),
        new(SettingKeys.MessagingRemovedOriginalRetentionDays.Name, SettingValueKinds.Int, SettingCategories.Features, "180", Min: 30, Max: 730, ClientLevelOnly: true),
        new(SettingKeys.AnnouncementsMaxPerAuthorPerDay.Name, SettingValueKinds.Int, SettingCategories.Features, "10", Min: 1, Max: 100, ClientLevelOnly: true),
        // M5 wave 2: the school week and the conflict mode of the timetable builder.
        new(SettingKeys.TimetableWorkingDays.Name, SettingValueKinds.StringSet, SettingCategories.Academic,
            "[\"Sun\",\"Mon\",\"Tue\",\"Wed\",\"Thu\"]", AllowedValues: ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]),
        new(SettingKeys.TimetableConflictMode.Name, SettingValueKinds.Enum, SettingCategories.Academic, "\"Block\"",
            AllowedValues: ["Block", "Warn"]),
        // M5 wave 3: attendance and excuses.
        new(SettingKeys.AttendanceGenerateDaysAhead.Name, SettingValueKinds.Int, SettingCategories.Academic, "7", Min: 1, Max: 30),
        new(SettingKeys.AttendanceEditWindowHours.Name, SettingValueKinds.Int, SettingCategories.Academic, "48", Min: 1, Max: 720),
        new(SettingKeys.AttendanceLiveMinPercent.Name, SettingValueKinds.Int, SettingCategories.Academic, "50", Min: 1, Max: 100),
        new(SettingKeys.AttendanceLateAfterMinutes.Name, SettingValueKinds.Int, SettingCategories.Academic, "10", Min: 0, Max: 120),
        new(SettingKeys.AttendanceAbsentThreshold.Name, SettingValueKinds.Int, SettingCategories.Academic, "5", Min: 1, Max: 100),
        new(SettingKeys.AttendanceThresholdWindowDays.Name, SettingValueKinds.Int, SettingCategories.Academic, "30", Min: 7, Max: 365),
        new(SettingKeys.AttendanceLateWeightPercent.Name, SettingValueKinds.Int, SettingCategories.Academic, "50", Min: 0, Max: 100),
        new(SettingKeys.ExcusesSubmitters.Name, SettingValueKinds.Enum, SettingCategories.Academic, "\"GuardiansOnly\"",
            AllowedValues: ["GuardiansOnly", "GuardiansAndStudents"]),
        new(SettingKeys.ExcusesMaxDaysAfterAbsence.Name, SettingValueKinds.Int, SettingCategories.Academic, "5", Min: 1, Max: 60),
        new(SettingKeys.ExcusesAllowedReasons.Name, SettingValueKinds.StringSet, SettingCategories.Academic,
            "[\"Illness\",\"Family\",\"Official\",\"Other\"]", AllowedValues: Excuses.ExcuseReasons.All),
        new(SettingKeys.ExcusesAttachmentRequiredReasons.Name, SettingValueKinds.StringSet, SettingCategories.Academic, "[]",
            AllowedValues: Excuses.ExcuseReasons.All),
        new(SettingKeys.ExcusesMaxAttachmentMb.Name, SettingValueKinds.Int, SettingCategories.Files, "5", Min: 1, Max: 25),
        new(SettingKeys.ExcusesAttachmentRetentionDays.Name, SettingValueKinds.Int, SettingCategories.Files, "365", Min: 30, Max: 3650),
    ];

    /// <summary>The definition with this exact (case-sensitive) name, or null.</summary>
    public static SettingDefinition? Find(string name) =>
        All.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
}
