using Lms.Domain.Abstractions;
using Lms.Domain.Identity;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Notifications;

public sealed class NotificationRecipientResolver(LmsDbContext db, IClock clock) : INotificationRecipientResolver
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        string eventKey, Guid organizationId, string orgUnitPath, CancellationToken cancellationToken = default)
    {
        var definition = NotificationEvents.Find(eventKey)
            ?? throw new ArgumentException($"Unknown notification event {eventKey}.", nameof(eventKey));
        if (definition.DefaultRecipientRoles.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var roleKeys = definition.DefaultRecipientRoles;
        var upward = definition.RecipientScope == RecipientScopes.AncestorsAndSelf;
        var ancestors = OrgUnitPath.AncestorsAndSelf(orgUnitPath);

        // ISOLATION-BYPASS: the event is raised by someone above or beside the recipients (a root admin changing a School
        // policy), so the subtree scope of the caller must not hide them. The client and soft-delete filters are
        // re-applied explicitly, and only assignments at or below the event unit (or, for AncestorsAndSelf events, at
        // or above it) are read.
        var users = await (
                from assignment in db.RoleAssignments.IgnoreQueryFilters()
                join role in db.Roles.IgnoreQueryFilters() on assignment.RoleId equals role.Id
                where !assignment.IsDeleted && assignment.OrganizationId == organizationId
                      && (upward ? ancestors.Contains(assignment.OrgUnitPath) : assignment.OrgUnitPath.StartsWith(orgUnitPath))
                      && (assignment.ValidFrom == null || assignment.ValidFrom <= today)
                      && (assignment.ValidTo == null || assignment.ValidTo >= today)
                      && !role.IsDeleted && role.OrganizationId == organizationId
                      && role.BuiltInKey != null && roleKeys.Contains(role.BuiltInKey)
                      && !db.Users.Any(u => u.Id == assignment.UserId && u.Status != UserStatus.Active)
                select assignment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return users;
    }
}
