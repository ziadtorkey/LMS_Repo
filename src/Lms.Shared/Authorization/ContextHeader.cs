namespace Lms.Shared.Authorization;

/// <summary>Active context header: carries the id of the active RoleAssignment of the caller.</summary>
public static class ContextHeader
{
    public const string Name = "X-Lms-Context";
}
