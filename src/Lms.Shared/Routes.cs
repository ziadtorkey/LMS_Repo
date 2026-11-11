namespace Lms.Shared;

/// <summary>
/// API route constants shared by Lms.Api and Lms.Client. <c>...Pattern</c> constants are endpoint templates for
/// the server; the static methods build concrete URLs for the client.
/// </summary>
public static partial class Routes
{
    public const string Me = "/api/me";
    public const string MyPermissions = "/api/me/permissions";

    // M1 organization (Task 1)
    public const string Clients = "/api/clients";
    public const string ClientPattern = Clients + "/{id:guid}";

    public const string LevelTypes = "/api/level-types";
    public const string LevelTypesOrder = LevelTypes + "/order";
    public const string LevelTypePattern = LevelTypes + "/{id:guid}";

    public const string OrgUnits = "/api/org-units";
    public const string OrgUnitPattern = OrgUnits + "/{id:guid}";
    public const string OrgUnitStatusPattern = OrgUnitPattern + "/status";
    public const string OrgUnitMovePattern = OrgUnitPattern + "/move";
    public const string OrgUnitAcademicFlagsPattern = OrgUnitPattern + "/academic-flags";

    public static string Client(Guid id) => $"{Clients}/{id}";
    public static string LevelType(Guid id) => $"{LevelTypes}/{id}";
    public static string OrgUnit(Guid id) => $"{OrgUnits}/{id}";
    public static string OrgUnitStatus(Guid id) => $"{OrgUnit(id)}/status";
    public static string OrgUnitMove(Guid id) => $"{OrgUnit(id)}/move";
    public static string OrgUnitAcademicFlags(Guid id) => $"{OrgUnit(id)}/academic-flags";

    // M1 settings, policies, branch copy and templates (Task 2)
    public const string PoliciesCatalog = "/api/policies/catalog";
    public const string SettingsCatalog = "/api/settings/catalog";
    public const string OrgUnitSettingsPattern = OrgUnitPattern + "/settings";
    public const string OrgUnitSettingPattern = OrgUnitSettingsPattern + "/{name}";
    public const string OrgUnitPoliciesPattern = OrgUnitPattern + "/policies";
    public const string OrgUnitPolicyPattern = OrgUnitPoliciesPattern + "/{key}";
    public const string OrgUnitsCopy = OrgUnits + "/copy";

    public const string Templates = "/api/templates";
    public const string TemplatePattern = Templates + "/{id:guid}";
    public const string TemplateApplyPattern = TemplatePattern + "/apply";

    public static string OrgUnitSettings(Guid id) => $"{OrgUnit(id)}/settings";
    public static string OrgUnitSetting(Guid id, string name) => $"{OrgUnitSettings(id)}/{Uri.EscapeDataString(name)}";
    public static string OrgUnitPolicies(Guid id) => $"{OrgUnit(id)}/policies";
    public static string OrgUnitPolicy(Guid id, string key) => $"{OrgUnitPolicies(id)}/{Uri.EscapeDataString(key)}";
    public static string Template(Guid id) => $"{Templates}/{id}";
    public static string TemplateApply(Guid id) => $"{Template(id)}/apply";

    // M1 roles, assignments and audit (Task 3)
    public const string Permissions = "/api/permissions";
    public const string Roles = "/api/roles";
    public const string RolePattern = Roles + "/{id:guid}";
    public const string Assignments = "/api/assignments";
    public const string AssignmentPattern = Assignments + "/{id:guid}";
    public const string OrgUnitAssignmentsPattern = OrgUnitPattern + "/assignments";
    public const string Audit = "/api/audit";
    public const string AuditEntryPattern = Audit + "/{id:guid}";
    public const string AuditExport = Audit + "/export";

    public static string Role(Guid id) => $"{Roles}/{id}";
    public static string Assignment(Guid id) => $"{Assignments}/{id}";
    public static string OrgUnitAssignments(Guid id) => $"{OrgUnit(id)}/assignments";
    public static string AuditEntry(Guid id) => $"{Audit}/{id}";

    // M1 academic years, calendar and notifications (Task 4)
    public const string AcademicYears = "/api/academic-years";
    public const string AcademicYearsEffective = AcademicYears + "/effective";
    public const string AcademicYearPattern = AcademicYears + "/{id:guid}";
    public const string AcademicYearActivatePattern = AcademicYearPattern + "/activate";
    public const string AcademicYearTermsPattern = AcademicYearPattern + "/terms";
    public const string AcademicYearCalendarPattern = AcademicYearPattern + "/calendar";
    public const string Terms = "/api/terms";
    public const string TermPattern = Terms + "/{id:guid}";
    public const string CalendarEvents = "/api/calendar-events";
    public const string CalendarEventPattern = CalendarEvents + "/{id:guid}";
    public const string MyNotifications = Me + "/notifications";
    public const string MyNotificationReadPattern = MyNotifications + "/{id:guid}/read";
    public const string MyNotificationsReadAll = MyNotifications + "/read-all";
    public const string MyNotificationPreferences = Me + "/notification-preferences";

    public static string AcademicYear(Guid id) => $"{AcademicYears}/{id}";
    public static string AcademicYearActivate(Guid id) => $"{AcademicYear(id)}/activate";
    public static string AcademicYearTerms(Guid id) => $"{AcademicYear(id)}/terms";
    public static string AcademicYearCalendar(Guid id) => $"{AcademicYear(id)}/calendar";
    public static string Term(Guid id) => $"{Terms}/{id}";
    public static string CalendarEvent(Guid id) => $"{CalendarEvents}/{id}";
    public static string MyNotificationRead(Guid id) => $"{MyNotifications}/{id}/read";

    // M2 staff (Task 2; student, guardian and change-request routes arrive in M3, D23)
    public const string Staff = "/api/staff";
    public const string StaffPattern = Staff + "/{id:guid}";
    public const string StaffStatusPattern = StaffPattern + "/status";
    public const string StaffSharesPattern = StaffPattern + "/shares";
    public const string StaffSharePattern = StaffSharesPattern + "/{assignmentId:guid}";
    public const string StaffResendInvitationPattern = StaffPattern + "/resend-invitation";

    public static string StaffMember(Guid id) => $"{Staff}/{id}";
    public static string StaffStatus(Guid id) => $"{StaffMember(id)}/status";
    public static string StaffShares(Guid id) => $"{StaffMember(id)}/shares";
    public static string StaffShare(Guid id, Guid assignmentId) => $"{StaffShares(id)}/{assignmentId}";
    public static string StaffResendInvitation(Guid id) => $"{StaffMember(id)}/resend-invitation";

    // M2 imports (Task 3, X-06). GET {kind} lists batches, POST {kind} uploads; {kind} from ImportKinds.
    public const string Imports = "/api/imports";
    public const string ImportKindPattern = Imports + "/{kind}";
    public const string ImportColumnsPattern = ImportKindPattern + "/columns";
    public const string ImportTemplatePattern = ImportKindPattern + "/template";
    public const string ImportBatchPattern = ImportKindPattern + "/{id:guid}";
    public const string ImportRowsPattern = ImportBatchPattern + "/rows";
    public const string ImportErrorReportPattern = ImportBatchPattern + "/error-report";
    public const string ImportCommitPattern = ImportBatchPattern + "/commit";

    public static string ImportsOfKind(string kind) => $"{Imports}/{Uri.EscapeDataString(kind)}";
    public static string ImportColumns(string kind) => $"{ImportsOfKind(kind)}/columns";
    public static string ImportTemplate(string kind, string culture) =>
        $"{ImportsOfKind(kind)}/template?culture={Uri.EscapeDataString(culture)}";
    public static string ImportBatch(string kind, Guid id) => $"{ImportsOfKind(kind)}/{id}";
    public static string ImportRows(string kind, Guid id) => $"{ImportBatch(kind, id)}/rows";
    public static string ImportErrorReport(string kind, Guid id) => $"{ImportBatch(kind, id)}/error-report";
    public static string ImportCommit(string kind, Guid id) => $"{ImportBatch(kind, id)}/commit";

    // M2 identity providers (Task 3, FR-SEC-08)
    public const string IdentityProviders = "/api/identity-providers";
    public const string IdentityProviderPattern = IdentityProviders + "/{id:guid}";
    public const string IdentityProviderTestPattern = IdentityProviderPattern + "/test";

    public static string IdentityProvider(Guid id) => $"{IdentityProviders}/{id}";
    public static string IdentityProviderTest(Guid id) => $"{IdentityProvider(id)}/test";
}
