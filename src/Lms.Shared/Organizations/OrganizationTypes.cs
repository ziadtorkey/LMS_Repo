namespace Lms.Shared.Organizations;

/// <summary>Type of a client organization (PRD §4.1). String values, like <c>RoleBaseKinds</c>.</summary>
public static class OrganizationTypes
{
    public const string SchoolGroup = "SchoolGroup";
    public const string University = "University";

    public static IReadOnlyList<string> All { get; } = [SchoolGroup, University];
}

/// <summary>Status of a client organization; contexts of a suspended client are rejected.</summary>
public static class OrganizationStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";

    public static IReadOnlyList<string> All { get; } = [Active, Suspended];
}

/// <summary>Status of an org unit (D15: an inactive unit hides the contexts below it).</summary>
public static class OrgUnitStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";

    public static IReadOnlyList<string> All { get; } = [Active, Inactive];
}
