using Lms.Shared.People;

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

    // M3 (students D25, FR-STU-04, FR-STU-05, FR-CLS-01, FR-LP-01, FR-ENR-04)
    /// <summary><c>Auto</c> (generated from <see cref="StudentsCodePattern"/>) or <c>Manual</c> (D25).</summary>
    public static readonly SettingKey<string> StudentsCodeMode = new("Students.CodeMode", StudentCodeModes.Auto);

    /// <summary>Literal <c>[A-Za-z0-9-]</c>, optional <c>{RootCode}</c>, <c>{Seq:N}</c> exactly once (D25).</summary>
    public static readonly SettingKey<string> StudentsCodePattern = new("Students.CodePattern", "{Seq:6}");

    /// <summary>Student fields whose change needs approval (a subset of <see cref="StudentFields.Requestable"/>).</summary>
    public static readonly SettingKey<IReadOnlyList<string>> StudentsCriticalFields =
        new("Students.CriticalFields", StudentFields.DefaultCritical);

    /// <summary>Whether a student must have at least one complete guardian (FR-STU-05).</summary>
    public static readonly SettingKey<bool> GuardiansDataRequired = new("Guardians.DataRequired", false);

    /// <summary><c>Warn</c> (confirm to go over) or <c>Block</c> when a class is full.</summary>
    public static readonly SettingKey<string> ClassesCapacityMode = new("Classes.CapacityMode", "Warn");

    /// <summary>Label of a blueprint section: <c>Week</c>, <c>Unit</c> or <c>Chapter</c>.</summary>
    public static readonly SettingKey<string> BlueprintsSectionLabel = new("Blueprints.SectionLabel", "Week");

    /// <summary>Whether grades follow a transferred student (read by the M5 gradebook).</summary>
    public static readonly SettingKey<bool> EnrollmentTransferCarriesGrades = new("Enrollment.TransferCarriesGrades", true);

    // M4a (FR-ASG-01, FR-QZ-02)
    /// <summary>The penalty a new <c>Penalty</c>-policy assignment starts with (0–100 %); the teacher may change it.</summary>
    public static readonly SettingKey<int> AssignmentsLatePenaltyDefaultPercent = new("Assignments.LatePenaltyDefaultPercent", 10);

    /// <summary>Seconds after an attempt's <c>EndsAtUtc</c> during which a last autosave or submit is still taken (0–300).</summary>
    public static readonly SettingKey<int> QuizzesAutoSubmitGraceSeconds = new("Quizzes.AutoSubmitGraceSeconds", 30);

    // M4b wave 3b: package limits (per client; the fixed limits are in code, see the M4b plan Decision 9).
    /// <summary>Most entries an archive may hold.</summary>
    public static readonly SettingKey<int> PackagesMaxEntries = new("Packages.MaxEntries", 10_000);

    /// <summary>Most uncompressed megabytes an archive may expand to.</summary>
    public static readonly SettingKey<int> PackagesMaxUncompressedMb = new("Packages.MaxUncompressedMb", 1024);

    /// <summary>Extracted package storage a client may hold, in megabytes.</summary>
    public static readonly SettingKey<int> PackagesMaxClientStorageMb = new("Packages.MaxClientStorageMb", 10_240);

    /// <summary>Lifetime of a package launch token in minutes (the session; hard cap 480).</summary>
    public static readonly SettingKey<int> PackagesTokenMinutes = new("Packages.TokenMinutes", 120);

    /// <summary>Smallest interval between two tracking commits of one attempt, in seconds.</summary>
    public static readonly SettingKey<int> PackagesCommitMinIntervalSeconds = new("Packages.CommitMinIntervalSeconds", 2);
    // M4b live sessions (FR-VC-01, D53, D55; M4b plan Decisions 4 and 9). Live.MaxHostSharePercent is platform-level and
    // lives in server configuration (LiveSessionLimits), not here: no client admin may change it.
    /// <summary>Minutes before the start during which students may join (0–60); the window closes at the scheduled end.</summary>
    public static readonly SettingKey<int> LiveJoinWindowMinutes = new("Live.JoinWindowMinutes", 15);

    /// <summary>Minutes before the start at which the in-app reminder is sent (5–1440).</summary>
    public static readonly SettingKey<int> LiveReminderMinutes = new("Live.ReminderMinutes", 30);

    /// <summary>
    /// Zoom meetings a client may create per UTC day (1–10 000), read at the client's root unit: a weekly series counts
    /// one per occurrence, updates and deletes do not count; a series that would cross the cap is refused whole.
    /// </summary>
    public static readonly SettingKey<int> LiveMaxCreatesPerDay = new("Live.MaxCreatesPerDay", 500);

    /// <summary>
    /// Whether Zoom meetings of ClassCourses under the unit are created with cloud recording (D55, default off). Read at
    /// the ClassCourse's unit; changed only with <c>Settings.Update</c>.
    /// </summary>
    public static readonly SettingKey<bool> LiveCloudRecordingEnabled = new("Live.CloudRecordingEnabled", false);

    // M5 wave 1 (M5 plan Task 1: L2, L8, D58, D62, D64).
    /// <summary>The client's IANA time zone (client level, never overridden by a unit); <c>Asia/Riyadh</c> also when the setting is missing (D64).</summary>
    public static readonly SettingKey<string> OrgTimeZone = new("Org.TimeZone", "Asia/Riyadh");

    /// <summary>What the engine does with a missing score: <c>Normalise</c>, <c>Zero</c> or <c>HideUntilComplete</c> (D58).</summary>
    public static readonly SettingKey<string> GradesMissingPolicy = new("Grades.MissingPolicy", "Normalise");

    /// <summary>How a category's items combine: <c>PointsWeighted</c> or <c>PercentAverage</c> (D58).</summary>
    public static readonly SettingKey<string> GradesCategoryMethod = new("Grades.CategoryMethod", "PointsWeighted");

    /// <summary>Decimal places of computed grades (0-4).</summary>
    public static readonly SettingKey<int> GradesDecimalPlaces = new("Grades.DecimalPlaces", 2);

    /// <summary>Terms per academic year, 2 or 3 (client level, D62).</summary>
    public static readonly SettingKey<int> AcademicYearsTermsPerYear = new("AcademicYears.TermsPerYear", 2);

    // M6 wave 1 (D66 defaults; every Comms.* and Messaging.* setting is client level, edited by the Org Admin at the client root).
    /// <summary>Whether the client's notices may use email at all.</summary>
    public static readonly SettingKey<bool> CommsEmailEnabled = new("Comms.EmailEnabled", true);

    /// <summary>Whether the client's notices may use Web Push.</summary>
    public static readonly SettingKey<bool> CommsPushEnabled = new("Comms.PushEnabled", false);

    /// <summary>Daily caps per client (D66: 5,000 email, 20,000 push; D76: no SMS), counted in the client's day; the admin may raise them up to a platform ceiling.</summary>
    public static readonly SettingKey<int> CommsMaxEmailPerDay = new("Comms.MaxEmailPerDay", 5000);

    public static readonly SettingKey<int> CommsMaxPushPerDay = new("Comms.MaxPushPerDay", 20000);

    /// <summary>Daily caps per recipient and channel.</summary>
    public static readonly SettingKey<int> CommsMaxEmailPerUserPerDay = new("Comms.MaxEmailPerUserPerDay", 20);

    public static readonly SettingKey<int> CommsMaxPushPerUserPerDay = new("Comms.MaxPushPerUserPerDay", 50);

    /// <summary>Quiet hours hold push (never email, in-app, mandatory or urgent events); off by default, 21:00-07:00 in the client time zone when on.</summary>
    public static readonly SettingKey<bool> CommsQuietHoursEnabled = new("Comms.QuietHours.Enabled", false);

    public static readonly SettingKey<string> CommsQuietHoursStart = new("Comms.QuietHours.Start", "21:00");
    public static readonly SettingKey<string> CommsQuietHoursEnd = new("Comms.QuietHours.End", "07:00");

    /// <summary>Days delivery rows are kept (D66: 90).</summary>
    public static readonly SettingKey<int> CommsDeliveryRetentionDays = new("Comms.DeliveryRetentionDays", 90);

    /// <summary>Whether participants see "seen by" (off by default; staff only in groups, students never see staff read state).</summary>
    public static readonly SettingKey<bool> MessagingReadReceipts = new("Messaging.ReadReceipts", false);

    public static readonly SettingKey<int> MessagingMaxPerMinute = new("Messaging.MaxPerMinute", 30);
    public static readonly SettingKey<int> MessagingMaxNewConversationsPerDay = new("Messaging.MaxNewConversationsPerDay", 20);
    public static readonly SettingKey<int> MessagingMaxGroupSize = new("Messaging.MaxGroupSize", 50);

    /// <summary>Per-user cap of message attachment uploads a minute (429 <c>rate_limited</c> over it).</summary>
    public static readonly SettingKey<int> MessagingUploadsPerMinute = new("Messaging.UploadsPerMinute", 10);

    /// <summary>Per-author cap of announcements a day.</summary>
    public static readonly SettingKey<int> AnnouncementsMaxPerAuthorPerDay = new("Announcements.MaxPerAuthorPerDay", 10);

    // M5 wave 2 (M5 plan Task 2).
    /// <summary>The school week as a JSON array of <c>WorkingDayNames</c>, Sunday to Thursday by default (D58); read through the unit hierarchy.</summary>
    public static readonly SettingKey<IReadOnlyList<string>> TimetableWorkingDays = new("Timetable.WorkingDays", ["Sun", "Mon", "Tue", "Wed", "Thu"]);

    /// <summary>What a slot save does with a clash: <c>Block</c> (default) or <c>Warn</c> (FR-TT-03).</summary>
    public static readonly SettingKey<string> TimetableConflictMode = new("Timetable.ConflictMode", "Block");
    // M5 wave 3 (M5 plan Task 3: D58, D61, L11, L13, L14).
    /// <summary>Days of timetable sessions created ahead by the generator (1-30).</summary>
    public static readonly SettingKey<int> AttendanceGenerateDaysAhead = new("Attendance.GenerateDaysAhead", 7);

    /// <summary>Hours after a session is taken during which the teacher may still edit it (L14).</summary>
    public static readonly SettingKey<int> AttendanceEditWindowHours = new("Attendance.EditWindowHours", 48);

    /// <summary>A live participant counts as present at this percent of the scheduled length (L11).</summary>
    public static readonly SettingKey<int> AttendanceLiveMinPercent = new("Attendance.LiveMinPercent", 50);

    /// <summary>A live participant joining this many minutes after the start is proposed Late.</summary>
    public static readonly SettingKey<int> AttendanceLateAfterMinutes = new("Attendance.LateAfterMinutes", 10);

    /// <summary>Absence alert: the score at which the counselor is alerted (D61: 5 in 30 days).</summary>
    public static readonly SettingKey<int> AttendanceAbsentThreshold = new("Attendance.AbsentThreshold", 5);

    /// <summary>Absence alert: the window in days.</summary>
    public static readonly SettingKey<int> AttendanceThresholdWindowDays = new("Attendance.ThresholdWindowDays", 30);

    /// <summary>Absence alert: a Late counts as this percent of an absence (D61: half).</summary>
    public static readonly SettingKey<int> AttendanceLateWeightPercent = new("Attendance.LateWeightPercent", 50);

    /// <summary>Who may submit an excuse: <c>GuardiansOnly</c> or <c>GuardiansAndStudents</c> (D61).</summary>
    public static readonly SettingKey<string> ExcusesSubmitters = new("Excuses.Submitters", "GuardiansOnly");

    /// <summary>School days after an absence within which an excuse may be submitted (L13).</summary>
    public static readonly SettingKey<int> ExcusesMaxDaysAfterAbsence = new("Excuses.MaxDaysAfterAbsence", 5);

    /// <summary>The reasons a submitter may choose (a subset of <c>ExcuseReasons</c>, default all).</summary>
    public static readonly SettingKey<IReadOnlyList<string>> ExcusesAllowedReasons = new("Excuses.AllowedReasons", Excuses.ExcuseReasons.All);

    /// <summary>The reasons that need an attachment (default none).</summary>
    public static readonly SettingKey<IReadOnlyList<string>> ExcusesAttachmentRequiredReasons = new("Excuses.AttachmentRequiredReasons", Array.Empty<string>());

    /// <summary>Largest excuse attachment in MB (L10).</summary>
    public static readonly SettingKey<int> ExcusesMaxAttachmentMb = new("Excuses.MaxAttachmentMb", 5);

    /// <summary>Days after which the attachments of rejected or withdrawn excuses are purged by the retention job (L10, Task 19); the excuse stays readable.</summary>
    public static readonly SettingKey<int> ExcusesAttachmentRetentionDays = new("Excuses.AttachmentRetentionDays", 365);
}
