namespace Lms.Domain.Access;

public interface IAccessEvaluator
{
    /// <summary>
    /// Decides whether the current caller may use <paramref name="permission"/>. Order: authenticated, active role
    /// grants the permission, resource inside the caller's scope, no ancestor policy blocks it.
    /// A null resource path means "the caller's own scope path".
    /// </summary>
    Task<AccessDecision> EvaluateAsync(string permission, string? resourceOrgUnitPath, CancellationToken cancellationToken);
}