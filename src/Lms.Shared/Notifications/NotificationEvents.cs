using Lms.Shared.Authorization;

namespace Lms.Shared.Notifications;

/// <summary>Delivery channels (FR-NOT-01). M1 delivers InApp only; the others are stored preferences for M6.</summary>
public static class NotificationChannels
{
    public const string InApp = "InApp";
    public const string Email = "Email";
    public const string Sms = "Sms";
    public const string Push = "Push";

    public static IReadOnlyList<string> All { get; } = [InApp, Email, Sms, Push];
}

/// <summary>Where the default recipient roles of an event are looked up, relative to the event's unit.</summary>
public static class RecipientScopes
{
    /// <summary>Role holders at the event's unit and below (M1 behaviour).</summary>
    public const string Subtree = "Subtree";

    /// <summary>Role holders at the event's unit and its ancestors (e.g. admins above a staff member's home unit, D35).</summary>
    public const string AncestorsAndSelf = "AncestorsAndSelf";

    public static IReadOnlyList<string> All { get; } = [Subtree, AncestorsAndSelf];
}

/// <summary>
/// A notification event. <c>DefaultRecipientRoles</c> are built-in role keys reached from the event's unit as
/// <c>RecipientScope</c> says (<see cref="RecipientScopes"/>); empty = the publisher names the recipients (e.g. the
/// assignee). <c>Parameters</c> must all be supplied on publish.
/// </summary>
public sealed record NotificationEventDefinition(
    string Key,
    IReadOnlyList<string> DefaultRecipientRoles,
    IReadOnlyList<string> Parameters,
    string RecipientScope = RecipientScopes.Subtree);

/// <summary>Notification event catalog ("Module.Event" keys). Later milestones append; never rename a key.</summary>
public static class NotificationEvents
{
    public const string AccessRoleAssigned = "Access.RoleAssigned";
    public const string AccessRoleRemoved = "Access.RoleRemoved";
    public const string OrganizationPolicyChanged = "Organization.PolicyChanged";

    // M2 (D35 staff expiry, X-06 imports). Student change-request events arrive with the M3 contract (D23).
    public const string StaffExpiringSoon = "Staff.ExpiringSoon";
    public const string StaffExpired = "Staff.Expired";
    public const string ImportsCompleted = "Imports.Completed";

    public static IReadOnlyList<NotificationEventDefinition> All { get; } =
    [
        new(AccessRoleAssigned, [], ["RoleNameAr", "RoleNameEn", "OrgUnitNameAr", "OrgUnitNameEn"]),
        new(AccessRoleRemoved, [], ["RoleNameAr", "RoleNameEn", "OrgUnitNameAr", "OrgUnitNameEn"]),
        new(OrganizationPolicyChanged, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            ["PolicyKey", "OrgUnitNameAr", "OrgUnitNameEn", "State"]),
        new(StaffExpiringSoon, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            ["StaffNameAr", "StaffNameEn", "ExpiresOn"], RecipientScopes.AncestorsAndSelf),
        new(StaffExpired, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            ["StaffNameAr", "StaffNameEn", "ExpiresOn"], RecipientScopes.AncestorsAndSelf),
        // Named recipient: the user who requested the import.
        new(ImportsCompleted, [], ["Kind", "Status", "RowCount"]),
    ];

    /// <summary>The definition with this exact (case-sensitive) key, or null.</summary>
    public static NotificationEventDefinition? Find(string key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));
}
