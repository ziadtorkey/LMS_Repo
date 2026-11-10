namespace Lms.Shared.Authorization;

/// <summary>
/// Base kind of a role (PRD §3.2); decides which relationship rules apply.
/// String values keep Lms.Shared free of Domain types; the Domain RoleBaseKind enum uses the same names.
/// </summary>
public static class RoleBaseKinds
{
    public const string Staff = "Staff";
    public const string Student = "Student";
    public const string Guardian = "Guardian";
}
