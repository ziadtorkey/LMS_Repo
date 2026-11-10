using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Lms.Api.Features.Notifications;

/// <summary>
/// Push channel of the in-app notification center. The server only pushes (<see cref="NotificationHub.ReceiveMethod"/>);
/// clients send nothing. Connecting needs an authenticated user (the route is mapped with RequireAuthorization).
/// <para>
/// A connection belongs to the context it was opened with (<c>?context=assignmentId</c>, validated by the same
/// middleware as <c>X-Lms-Context</c>) and joins the group of (user, client) for it, so a person working in client A
/// never hears about client B. To switch context the client reconnects with the other assignment id.
/// </para>
/// </summary>
public sealed class NotificationsHub(LmsDbContext db, IClock clock) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // The context middleware already refused a malformed or foreign ?context= with 403 when this connection was
        // opened. The hub looks the assignment up again (a hub call has its own scope, so it cannot reuse the request
        // scoped context) to learn which client the connection works in.
        Guid? organizationId = null;
        var header = Context.GetHttpContext()!.Request.Headers[ContextHeader.Name];
        if (header.Count > 0)
        {
            var today = DateOnly.FromDateTime(clock.UtcNow);
            var assignmentId = Guid.TryParse(header[0], out var parsed) ? parsed : Guid.Empty;
            var userId = Guid.TryParse(Context.UserIdentifier, out var user) ? user : Guid.Empty;
            var assignment = await db.ValidAssignmentsFor(userId, today)
                .Where(a => a.AssignmentId == assignmentId)
                .FirstOrDefaultAsync();
            if (assignment is null)
            {
                Context.Abort();
                return;
            }

            organizationId = assignment.OrganizationId;
        }

        if (Guid.TryParse(Context.UserIdentifier, out var caller))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.For(caller, organizationId));
        }

        await base.OnConnectedAsync();
    }
}

public static class NotificationGroups
{
    /// <summary>The group of one user in one client; connections without a client context share the "none" group.</summary>
    public static string For(Guid userId, Guid? organizationId) =>
        $"notifications:{userId:N}:{(organizationId is { } id ? id.ToString("N") : "none")}";
}

/// <summary>Targets a user by the "sub" claim of the token, the same id the notification rows are stored under.</summary>
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst("sub")?.Value;
}

public sealed class SignalRNotificationPusher(IHubContext<NotificationsHub> hub) : INotificationPusher
{
    public Task PushAsync(Guid userId, Guid? organizationId, NotificationDto notification, CancellationToken cancellationToken) =>
        hub.Clients.Group(NotificationGroups.For(userId, organizationId))
            .SendAsync(NotificationHub.ReceiveMethod, notification, cancellationToken);
}
