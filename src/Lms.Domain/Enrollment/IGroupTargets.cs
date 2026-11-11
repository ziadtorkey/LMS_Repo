namespace Lms.Domain.Enrollment;

/// <summary>
/// Used by content targeting (M4) to validate a target group: the group must be live and belong to the class course the
/// content is published in. Groups are the only way to address part of a class course (D6).
/// </summary>
public interface IGroupTargets
{
    Task<bool> IsValidTargetAsync(Guid classCourseId, Guid groupId, CancellationToken cancellationToken);
}
