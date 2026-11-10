namespace Lms.Shared.Organizations;

/// <summary>A client organization as seen by the Platform Admin (FR-ORG-01).</summary>
public sealed record ClientDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Type,
    string Status,
    Guid RootOrgUnitId,
    DateTime CreatedAt);

/// <summary>Body of POST /api/clients: the client, its root level and unit, and its first Org Admin.</summary>
public sealed record CreateClientRequest(
    string NameAr,
    string NameEn,
    string Type,
    string RootLevelNameAr,
    string RootLevelNameEn,
    string RootUnitCode,
    string AdminEmail,
    string AdminDisplayNameAr,
    string AdminDisplayNameEn);

/// <summary>Body of PUT /api/clients/{id}.</summary>
public sealed record UpdateClientRequest(string NameAr, string NameEn, string Status);
