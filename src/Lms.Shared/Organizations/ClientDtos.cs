namespace Lms.Shared.Organizations;

/// <summary>A client organization as seen by the Platform Admin (FR-ORG-01).</summary>
public sealed record ClientDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Type,
    string Status,
    Guid RootOrgUnitId,
    DateTime CreatedAt,
    string ShortCode = "");

/// <summary>
/// Body of POST /api/clients: the client, its root level and unit, and its first Org Admin. <c>ShortCode</c> (D47) is the
/// client's login suffix for student sign-in names (<c>code@shortcode</c>): 2–20 lower-case letters, digits or '-',
/// unique across the platform; null = derived from <c>RootUnitCode</c>.
/// </summary>
public sealed record CreateClientRequest(
    string NameAr,
    string NameEn,
    string Type,
    string RootLevelNameAr,
    string RootLevelNameEn,
    string RootUnitCode,
    string AdminEmail,
    string AdminDisplayNameAr,
    string AdminDisplayNameEn,
    string? ShortCode = null);

/// <summary>Body of PUT /api/clients/{id}.</summary>
public sealed record UpdateClientRequest(string NameAr, string NameEn, string Status);
