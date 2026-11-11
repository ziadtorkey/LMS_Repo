using Lms.Domain.Common;

namespace Lms.Domain.Auditing;

/// <summary>
/// One immutable audit record. OrganizationId is null for events outside any client (platform admin, failed logins).
/// Entries are never soft-deleted.
/// </summary>
public sealed class AuditEntry : Entity, IOptionalTenantOwned
{
    private AuditEntry()
    {
        EntityType = string.Empty;
    }

    public AuditEntry(
        Guid? organizationId, Guid? userId, Guid? assignmentId, AuditAction action, string entityType, Guid entityId,
        string? beforeJson, string? afterJson, DateTime timestampUtc, string? ipAddress = null, string? orgUnitPath = null)
    {
        OrganizationId = organizationId;
        UserId = userId;
        AssignmentId = assignmentId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        TimestampUtc = timestampUtc;
        IpAddress = ipAddress;
        OrgUnitPath = orgUnitPath;
    }

    public Guid? OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? AssignmentId { get; private set; }
    public AuditAction Action { get; private set; }
    public string EntityType { get; private set; }
    public Guid EntityId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Path of the org unit the change happened at (the entity's own path when it is unit-scoped, the unit's path for an
    /// OrgUnit, else the actor's scope); null for platform-level events. Drives subtree visibility in the audit viewer.
    /// </summary>
    public string? OrgUnitPath { get; private set; }
}
