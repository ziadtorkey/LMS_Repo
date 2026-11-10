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

/// <summary>
/// A notification event. <c>DefaultRecipientRoles</c> are built-in role keys reached at the event's unit and below;
/// empty = the publisher names the recipients (e.g. the assignee). <c>Parameters</c> must all be supplied on publish.
/// </summary>
public sealed record NotificationEventDefinition(
    string Key,
    IReadOnlyList<string> DefaultRecipientRoles,
    IReadOnlyList<string> Parameters);

/// <summary>Notification event catalog ("Module.Event" keys). Later milestones append; never rename a key.</summary>
public static class NotificationEvents
{
    public const string AccessRoleAssigned = "Access.RoleAssigned";
    public const string AccessRoleRemoved = "Access.RoleRemoved";
    public const string OrganizationPolicyChanged = "Organization.PolicyChanged";

    public static IReadOnlyList<NotificationEventDefinition> All { get; } =
    [
        new(AccessRoleAssigned, [], ["RoleNameAr", "RoleNameEn", "OrgUnitNameAr", "OrgUnitNameEn"]),
        new(AccessRoleRemoved, [], ["RoleNameAr", "RoleNameEn", "OrgUnitNameAr", "OrgUnitNameEn"]),
        new(OrganizationPolicyChanged, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            ["PolicyKey", "OrgUnitNameAr", "OrgUnitNameEn", "State"]),
    ];

    /// <summary>The definition with this exact (case-sensitive) key, or null.</summary>
    public static NotificationEventDefinition? Find(string key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));
}
