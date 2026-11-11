using Lms.Domain.Access;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Shared.Notifications;

namespace Lms.Api.Features.Assignments;

/// <summary>Tells the assignee that a role was assigned or removed (FR-NOT-01/02). A notification failure never undoes the change.</summary>
public sealed class AssignmentNotifier(INotificationPublisher publisher, ILogger<AssignmentNotifier> logger)
{
    public Task RoleAssignedAsync(RoleAssignment assignment, Role role, OrgUnit unit, CancellationToken ct) =>
        SendAsync(NotificationEvents.AccessRoleAssigned, assignment, role, unit, "/choose-context", ct);

    public Task RoleRemovedAsync(RoleAssignment assignment, Role role, OrgUnit unit, CancellationToken ct) =>
        SendAsync(NotificationEvents.AccessRoleRemoved, assignment, role, unit, null, ct); // nothing left to open

    private async Task SendAsync(string eventKey, RoleAssignment assignment, Role role, OrgUnit unit, string? deepLink, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(new NotificationRequest(
                eventKey, assignment.OrganizationId, [assignment.UserId],
                new Dictionary<string, string>
                {
                    ["RoleNameAr"] = role.NameAr,
                    ["RoleNameEn"] = role.NameEn,
                    ["OrgUnitNameAr"] = unit.NameAr,
                    ["OrgUnitNameEn"] = unit.NameEn,
                },
                DeepLink: deepLink), ct);
        }
        catch (Exception ex)
        {
            // The assignment change is already saved; the assignee simply misses the notification.
            logger.LogWarning(ex, "Notification {Event} for assignment {AssignmentId} could not be published", eventKey, assignment.Id);
        }
    }
}
