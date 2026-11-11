namespace Lms.Domain.Access;

/// <summary>Resolves the relationship filter of the current caller for a permission (M3 Task 12).</summary>
public interface IRelationshipScope
{
    /// <summary>
    /// Unrestricted unless the active assignment's built-in role has a relationship rule for the permission
    /// (<c>RelationshipRules.For</c>). Computed once per request and relationship.
    /// </summary>
    Task<RelationshipFilter> ForAsync(string permission, CancellationToken cancellationToken);
}
