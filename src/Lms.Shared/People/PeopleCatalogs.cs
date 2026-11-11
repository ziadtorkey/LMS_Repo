namespace Lms.Shared.People;

/// <summary>
/// Status of a per-client person profile (staff in M2; student and guardian in M3). Only <c>Active</c> profiles keep
/// the role assignments of their base kind valid (M2 plan, person status gate).
/// </summary>
public static class PersonStatuses
{
    public const string Active = "Active";
    public const string Deactivated = "Deactivated";
    public const string Expired = "Expired";

    public static IReadOnlyList<string> All { get; } = [Active, Deactivated, Expired];
}

/// <summary>Accepted values of <c>StaffQuery.Sort</c> (a leading <c>-</c> sorts descending).</summary>
public static class StaffSorts
{
    public const string Name = "name";
    public const string NameDesc = "-name";
    public const string ExpiresOn = "expiresOn";
    public const string ExpiresOnDesc = "-expiresOn";

    public static IReadOnlyList<string> All { get; } = [Name, NameDesc, ExpiresOn, ExpiresOnDesc];
}
