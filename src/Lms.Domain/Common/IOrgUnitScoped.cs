namespace Lms.Domain.Common;

/// <summary>Row belongs to one org unit. Filtered by the caller's scope path (subtree).</summary>
public interface IOrgUnitScoped : ITenantOwned
{
    Guid OrgUnitId { get; }
    string OrgUnitPath { get; }
}