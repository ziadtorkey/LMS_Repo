using Lms.Domain.Identity;
using Lms.Domain.Organizations;

namespace Lms.Domain.People;

/// <summary>Finds or creates the platform-level user behind a person.</summary>
public interface IPersonAccounts
{
    /// <summary>
    /// The user with the (normalized) email, else the one with the user name; created without a password when neither
    /// exists. Uses the request's DbContext, so it joins the caller's transaction.
    /// </summary>
    Task<(User User, bool Created)> EnsureUserAsync(
        string? email, string userName, string nameAr, string nameEn, CancellationToken cancellationToken);
}

/// <summary>Collects invitations during a handler and sends them after the handler's transaction committed.</summary>
public interface IInvitationOutbox
{
    /// <param name="toEmail">Send here instead of the user's own email (an alternate address); null = the user's email.</param>
    void Enqueue(Guid userId, string culture, string? toEmail = null);

    /// <summary>Sends the queued invitations to users that still have no password. A failed send is logged, never thrown.</summary>
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>Status changes of profiles, with the history row and the user's derived status.</summary>
public interface IPersonStatusService
{
    /// <summary>Writes the history row, sets the profile status, saves, then recomputes the user's status. Staff and student only.</summary>
    Task ChangeAsync(PersonKind kind, Guid profileId, PersonStatus to, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Derives <c>User.Status</c> from the user's profiles in every client: any active profile (or none at all) means
    /// Active; otherwise Expired when every inactive profile is expired, else Deactivated. Platform admins and
    /// withdrawn users are left alone.
    /// </summary>
    Task RecomputeUserStatusAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Keeps one assignment of a built-in role per unit (student and guardian roles).</summary>
public interface IRoleAssignmentSync
{
    /// <summary>
    /// The user's live assignments of the built-in role in the client become exactly one per given unit: missing ones are
    /// added, extra ones soft-deleted. Saves.
    /// </summary>
    Task SyncBuiltInAsync(
        Guid userId, Guid organizationId, string builtInRoleKey, IReadOnlyCollection<OrgUnit> units, CancellationToken cancellationToken);
}
