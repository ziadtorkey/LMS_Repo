using Lms.Domain.Common;

namespace Lms.Domain.Assignments;

/// <summary>A teacher file attached to an assignment (an asset of the same class course).</summary>
public sealed class AssignmentAttachment : UnitOwnedEntity
{
    private AssignmentAttachment()
    {
    }

    public AssignmentAttachment(Assignment assignment, Guid assetId, int order)
    {
        PlaceLike(assignment);
        AssignmentId = assignment.Id;
        AssetId = assetId;
        Order = order;
    }

    public Guid AssignmentId { get; private set; }
    public Guid AssetId { get; private set; }
    public int Order { get; private set; }
}
