using Lms.Domain.Organizations;

namespace Lms.Domain.Common;

/// <summary>
/// Auditable, soft-deletable row that belongs to one org unit. Academic rows copy the unit fields from the row they hang
/// off (a course, a class, a class course), so they follow its visibility and are re-pathed with it.
/// </summary>
public abstract class UnitOwnedEntity : AuditableEntity, IOrgUnitScoped
{
    protected UnitOwnedEntity()
    {
        OrgUnitPath = string.Empty;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }

    protected void PlaceIn(OrgUnit unit) => PlaceIn(unit.OrganizationId, unit.Id, unit.Path);

    protected void PlaceLike(IOrgUnitScoped parent) => PlaceIn(parent.OrganizationId, parent.OrgUnitId, parent.OrgUnitPath);

    protected void PlaceIn(Guid organizationId, Guid orgUnitId, string orgUnitPath)
    {
        OrganizationId = organizationId;
        OrgUnitId = orgUnitId;
        OrgUnitPath = orgUnitPath;
    }

    /// <summary>Moves the row to another unit of the same client (used when a parent row changes owner).</summary>
    public void MoveTo(OrgUnit unit)
    {
        if (unit.OrganizationId != OrganizationId)
        {
            throw new InvalidOperationException("A row cannot move to another client.");
        }

        PlaceIn(unit);
    }
}
