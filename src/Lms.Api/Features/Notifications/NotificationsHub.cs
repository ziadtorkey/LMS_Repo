using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Notifications;

/// <summary>Names of the SignalR groups live pushes are sent to.</summary>
public static class NotificationGroups
{
    /// <summary>The user's connections that are working in this client.</summary>
    public static string For(Guid userId, Guid organizationId) => $"notifications:{userId:N}:{organizationId:N}";

    /// <summary>Notifications that belong to no client; every connection of the user is in this group.</summary>
    public static string NoClient(Guid userId) => $"notifications:{userId:N}";
}

/// <summary>
/// Push channel of the in-app notification center. Clients only receive (<see cref="NotificationHub.ReceiveMethod"/>).
/// A connection joins the group of the client of its active context, given as <c>?context=&lt;assignmentId&gt;</c> (or the
/// X-Lms-Context header) when connecting, and moves with <see cref="SwitchContext"/>; the assignment is always validated
/// against the caller, so a push never reaches a client the user is not currently working in.
/// </summary>
public sealed class NotificationsHub(LmsDbContext db, IClock clock) : Hub
{
    public const string ContextQueryParameter = "context";
    private const string CurrentGroupKey = "notification-group";

    public override async Task OnConnectedAsync()
    {
        if (UserId() is not { } userId)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, NotificationGroups.NoClient(userId));
        var http = Context.GetHttpContext();
        var raw = http?.Request.Query[ContextQueryParameter].FirstOrDefault()
            ?? http?.Request.Headers[ContextHeader.Name].FirstOrDefault();
        if (raw is not null && (!Guid.TryParse(raw, out var assignmentId) || !await JoinAsync(userId, assignmentId)))
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Moves this connection to the client of another of the caller's valid assignments.</summary>
    public async Task SwitchContext(Guid assignmentId)
    {
        if (UserId() is not { } userId || !await JoinAsync(userId, assignmentId))
        {
            throw new HubException("invalid_context");
        }
    }

    private async Task<bool> JoinAsync(Guid userId, Guid assignmentId)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var assignment = await db.ValidAssignmentsFor(userId, today)
            .Where(a => a.AssignmentId == assignmentId)
            .FirstOrDefaultAsync(Context.ConnectionAborted);
        if (assignment is null)
        {
            return false;
        }

        if (Context.Items.TryGetValue(CurrentGroupKey, out var previous) && previous is string old)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, old);
        }

        var group = NotificationGroups.For(userId, assignment.OrganizationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        Context.Items[CurrentGroupKey] = group;
        return true;
    }

    private Guid? UserId() =>
        Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id) ? id : null;
}

public sealed class SignalRNotificationPusher(IHubContext<NotificationsHub> hub) : INotificationPusher
{
    public Task PushAsync(Guid userId, Guid? organizationId, NotificationDto notification, CancellationToken cancellationToken)
    {
        var group = organizationId is { } org ? NotificationGroups.For(userId, org) : NotificationGroups.NoClient(userId);
        return hub.Clients.Group(group).SendAsync(NotificationHub.ReceiveMethod, notification, cancellationToken);
    }
}
