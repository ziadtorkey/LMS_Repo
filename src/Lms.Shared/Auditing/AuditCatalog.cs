namespace Lms.Shared.Auditing;

/// <summary>Names of security events written with <c>IAuditWriter.WriteSecurityEventAsync</c> (stored as the entry's entity type).</summary>
public static class AuditSecurityEvents
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string PasswordResetRequested = "PasswordResetRequested";
    public const string PasswordReset = "PasswordReset";
    public const string InvitationAccepted = "InvitationAccepted";

    // M2 external sign-in (FR-SEC-08, D33)
    public const string ExternalLoginLinked = "ExternalLoginLinked";
    public const string ExternalLoginSucceeded = "ExternalLoginSucceeded";
    public const string ExternalLoginFailed = "ExternalLoginFailed";
    public const string PasswordChanged = "PasswordChanged";
}

/// <summary>
/// What the audit viewer can filter on (FR-AUD-03). Every <c>[Audited]</c> domain type must be listed in
/// <see cref="EntityTypes"/> (checked by a unit test in M1 Task 18). Append only.
/// </summary>
public static class AuditCatalog
{
    public static IReadOnlyList<string> EntityTypes { get; } =
    [
        "Organization", "LevelType", "OrgUnit", "Role", "RolePermission", "RoleAssignment", "OrgUnitSetting",
        "OrgUnitPolicy", "User", "OrgTemplate", "AcademicYear", "Term", "CalendarEvent", "StoredFile",
        // M2 people kernel (StudentProfile, GuardianProfile, StudentGuardian are data model only until M3, D23),
        // imports and identity providers.
        "StaffProfile", "StudentProfile", "GuardianProfile", "StudentGuardian", "ImportBatch", "IdentityProviderConfig",
        // M3 students and academic structure.
        "StudentChangeRequest", "Course", "CourseAllocation", "CourseRestriction", "CourseSupervisor", "Blueprint",
        "Class", "ClassStudent", "ClassCourse", "ClassCourseTeacher", "TeacherCourseEligibility", "CourseEnrollment",
        "EnrollmentException", "StudentGroup", "GroupMember", "StudentTransfer", "ClassLessonPlan",
        // M4a wave 1: lesson modules, their simple content and assets (ModuleCompletion is not audited: volume).
        "LessonModule", "ModuleContent", "Asset",
        // M4a wave 2: activities and the M5 grade seam (submissions, answers and posts are not audited row by row:
        // volume; grading and moderation are).
        "Assignment", "SubmissionFeedback", "Quiz", "QuizQuestion", "LibraryResource", "ForumThread",
        "CourseAnnouncement", "GradeEvent",
        // M4b wave 3b: package uploads and their parsed SCORM / H5P content (attempts are not audited row by row: volume).
        "PackageUpload", "ScormPackage", "H5pContent",
        // M4b wave 3a: live sessions (participants and webhook receipts are not audited row by row: volume).
        "LiveSessionSeries", "LiveSession",
        // M5 wave 1: grading (overrides, publish changes and scale acknowledgements are the audited changes).
        "GradingScale", "WeightTemplate", "Gradebook", "GradeCategory", "GradeItem", "GradeEntry", "GradeSnapshot",
    ];

    public static IReadOnlyList<string> SecurityEvents { get; } =
    [
        AuditSecurityEvents.LoginSucceeded, AuditSecurityEvents.LoginFailed, AuditSecurityEvents.PasswordResetRequested,
        AuditSecurityEvents.PasswordReset, AuditSecurityEvents.InvitationAccepted,
        AuditSecurityEvents.ExternalLoginLinked, AuditSecurityEvents.ExternalLoginSucceeded,
        AuditSecurityEvents.ExternalLoginFailed,
        AuditSecurityEvents.PasswordChanged,
    ];
}

/// <summary>Audit actions; names match the Domain <c>AuditAction</c> enum.</summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string SecurityEvent = "SecurityEvent";

    public static IReadOnlyList<string> All { get; } = [Created, Updated, Deleted, SecurityEvent];
}
