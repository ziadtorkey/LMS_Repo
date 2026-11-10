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
}
