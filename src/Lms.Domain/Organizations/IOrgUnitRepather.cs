namespace Lms.Domain.Organizations;

/// <summary>Rewrites stored paths after a unit moved: every table that carries an org unit path, in the caller's transaction.</summary>
public interface IOrgUnitRepather
{
    /// <param name="organizationId">The client the move happened in; rows of other clients are never touched.</param>
    /// <param name="oldPrefix">The moved unit's former path.</param>
    /// <param name="newPrefix">The moved unit's new path.</param>
    /// <param name="depthDelta">New depth minus old depth, applied to the units below.</param>
    Task RepathAsync(Guid organizationId, string oldPrefix, string newPrefix, int depthDelta, CancellationToken cancellationToken);
}