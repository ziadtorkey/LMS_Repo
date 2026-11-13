using Lms.Domain.Common;

namespace Lms.Domain.Assignments;

/// <summary>A file of a submission (an asset of the class course uploaded by the student).</summary>
public sealed class SubmissionFile : UnitOwnedEntity
{
    private SubmissionFile()
    {
    }

    public SubmissionFile(Submission submission, Guid assetId)
    {
        PlaceLike(submission);
        SubmissionId = submission.Id;
        AssetId = assetId;
    }

    public Guid SubmissionId { get; private set; }
    public Guid AssetId { get; private set; }
}
