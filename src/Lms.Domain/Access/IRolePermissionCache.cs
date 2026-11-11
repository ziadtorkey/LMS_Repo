namespace Lms.Domain.Access;

/// <summary>Drops the cached permission set of a role so the next access check reads the saved one.</summary>
public interface IRolePermissionCache
{
    void Invalidate(Guid roleId);
}
