namespace Lms.Domain.Common;

/// <summary>
/// Row that may belong to a client or be platform-level (null OrganizationId).
/// Clients see only rows of their own organization; only platform admins see platform-level rows.
/// </summary>
public interface IOptionalTenantOwned
{
    Guid? OrganizationId { get; }
}