namespace Lms.Domain.Blueprints;

/// <summary>
/// Hook for modules that react to a blueprint version being published (after the commit). The class lesson plan module
/// registers one that pushes the version to existing class plans when the apply mode asks for it (FR-LP-04).
/// </summary>
public interface IBlueprintPublished
{
    /// <summary>Returns how many class plans are queued for the push (0 when none).</summary>
    Task<int> OnPublishedAsync(Blueprint blueprint, CancellationToken cancellationToken);
}
