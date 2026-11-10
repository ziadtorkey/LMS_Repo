namespace Lms.Shared;

/// <summary>
/// API route constants shared by Lms.Api and Lms.Client. <c>...Pattern</c> constants are endpoint templates for
/// the server; the static methods build concrete URLs for the client.
/// </summary>
public static class Routes
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
}
