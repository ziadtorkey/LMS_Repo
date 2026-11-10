namespace Lms.Shared.Auditing;

/// <summary>One row of the audit viewer (FR-AUD-03). <c>Action</c> from <see cref="AuditActions"/>.</summary>
public sealed record AuditEntryDto(
    Guid Id,
    DateTime TimestampUtc,
    Guid? UserId,
    string? UserEmail,
    string? UserDisplayNameAr,
    string? UserDisplayNameEn,
    string? RoleNameAr,
    string? RoleNameEn,
    string? OrgUnitNameAr,
    string? OrgUnitNameEn,
    string Action,
    string EntityType,
    Guid EntityId,
    string? IpAddress);

/// <summary>Response of GET /api/audit/{id}: the row plus the before/after JSON snapshots.</summary>
public sealed record AuditEntryDetailDto(AuditEntryDto Entry, string? BeforeJson, string? AfterJson);

/// <summary>
/// Filters of GET /api/audit and GET /api/audit/export, bound from the query string (<c>[AsParameters]</c>).
/// <c>OrganizationId</c> is honoured for the Platform Admin only.
/// </summary>
public sealed record AuditQuery(
    Guid? OrganizationId,
    Guid? UserId,
    string? EntityType,
    Guid? EntityId,
    string? Action,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>Values of the <c>format</c> query parameter of GET /api/audit/export.</summary>
public static class AuditExportFormats
{
    public const string Csv = "csv";
    public const string Xlsx = "xlsx";
}
