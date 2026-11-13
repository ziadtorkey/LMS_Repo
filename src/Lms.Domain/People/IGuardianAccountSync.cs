using Lms.Domain.Organizations;

namespace Lms.Domain.People;

/// <summary>What the guardian portal needs for one guardian: the units of their children and where the portal is switched on.</summary>
public sealed record GuardianPortalPlan(IReadOnlyList<OrgUnit> ChildUnits, IReadOnlyList<OrgUnit> EnabledUnits, bool HasEmail, bool IsActivated);

/// <summary>
/// Guardian portal accounts (FR-STU-06, D30). The guardian's user and Guardian-role assignments follow their children:
/// one assignment per unit where a live child is enrolled and <c>Features.GuardianPortal</c> is on; none when the portal is
/// off everywhere or the guardian has no email (the account stays). It sees the whole client, not just the caller's scope.
/// </summary>
public interface IGuardianAccountSync
{
    Task<GuardianPortalPlan> PlanAsync(GuardianProfile guardian, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the account in line with <see cref="PlanAsync"/>: creates the user (no password), the guardian's assignments
    /// per enabled unit, removes the others; sends the invitation for a new account, or on request while no password is set.
    /// </summary>
    Task SyncAsync(GuardianProfile guardian, bool resendInvitation, CancellationToken cancellationToken);
}
