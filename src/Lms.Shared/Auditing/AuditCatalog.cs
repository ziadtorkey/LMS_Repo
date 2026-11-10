namespace Lms.Shared.Auditing;

/// <summary>Names of security events written with <c>IAuditWriter.WriteSecurityEventAsync</c> (stored as the entry's entity type).</summary>
public static class AuditSecurityEvents
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string PasswordResetRequested = "PasswordResetRequested";
    public const string PasswordReset = "PasswordReset";
    public const string InvitationAccepted = "InvitationAccepted";
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
    ];

    public static IReadOnlyList<string> SecurityEvents { get; } =
    [
        AuditSecurityEvents.LoginSucceeded, AuditSecurityEvents.LoginFailed, AuditSecurityEvents.PasswordResetRequested,
        AuditSecurityEvents.PasswordReset, AuditSecurityEvents.InvitationAccepted,
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
