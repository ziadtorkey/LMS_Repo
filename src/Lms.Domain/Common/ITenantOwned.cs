namespace Lms.Domain.Common;

/// <summary>Row belongs to one client (organization). Filtered by the caller's organization.</summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}