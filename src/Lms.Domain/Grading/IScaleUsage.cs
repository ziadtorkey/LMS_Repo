namespace Lms.Domain.Grading;

/// <summary>
/// Whether a gradebook uses a scale (it then cannot be deleted: <c>scale_in_use</c>). The gradebook module (M5 Task 7) replaces the
/// default registration; until gradebooks exist nothing uses a scale.
/// </summary>
public interface IScaleUsage
{
    Task<bool> IsInUseAsync(Guid scaleId, CancellationToken cancellationToken);
}
