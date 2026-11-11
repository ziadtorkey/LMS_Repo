namespace Lms.Domain.Organizations;

/// <summary>
/// Extension point for deleting a unit (FR-ORG-04): later modules register a check per kind of data that hangs off a
/// unit (students, classes, courses). A non-null result is the reason key that blocks the delete.
/// </summary>
public interface IOrgUnitUsageCheck
{
    Task<string?> FindUsageAsync(OrgUnit unit, CancellationToken cancellationToken);
}