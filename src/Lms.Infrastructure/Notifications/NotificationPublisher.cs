using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Notifications;

/// <summary>
/// Writes through its own DbContext (a fresh one from <c>openDb</c>) so publishing never flushes, or fails with, the
/// pending changes of the caller: the notification of a change is sent after the change itself was saved.
/// </summary>
public sealed class NotificationPublisher(
    Func<LmsDbContext> openDb,
    ICurrentContext context,
    INotificationRecipientResolver resolver,
    INotificationPusher pusher,
    IClock clock) : INotificationPublisher
{
    public async Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        var definition = NotificationEvents.Find(request.EventKey)
            ?? throw new ArgumentException($"Unknown notification event {request.EventKey}.", nameof(request));
        var missing = definition.Parameters.Where(p => !request.Parameters.ContainsKey(p)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"Event {request.EventKey} needs the parameters: {string.Join(", ", missing)}.", nameof(request));
        }

        if (!NotificationDeepLinks.IsValid(request.DeepLink))
        {
            throw new ArgumentException("A deep link must be an app-relative path such as /org-units/1.", nameof(request));
        }

        var recipients = await RecipientsAsync(request, definition, cancellationToken);

        // Never the acting user, each recipient once.
        var userIds = recipients.Where(id => request.NotifyActor || id != context.UserId).Distinct().ToList();
        if (userIds.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var parameters = request.Parameters
            .Where(p => definition.Parameters.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        var parametersJson = JsonSerializer.Serialize(parameters);
        var rows = userIds
            .Select(id => new Notification(request.OrganizationId, id, request.EventKey, parametersJson, request.DeepLink, now))
            .ToList();

        await using (var db = openDb())
        {
            db.Set<Notification>().AddRange(rows);
            await db.SaveChangesAsync(cancellationToken);
        }

        await pusher.PushManyAsync(
            rows.Select(row => new PendingPush(
                row.UserId, row.OrganizationId,
                new NotificationDto(row.Id, row.EventKey, parameters, row.DeepLink, row.CreatedAt, IsRead: false))).ToList(),
            cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> RecipientsAsync(
        NotificationRequest request, NotificationEventDefinition definition, CancellationToken ct)
    {
        if (request.UserIds.Count > 0)
        {
            return request.UserIds;
        }

        if (definition.DefaultRecipientRoles.Count == 0 || request.OrganizationId is not { } organizationId
            || string.IsNullOrEmpty(request.OrgUnitPath))
        {
            throw new ArgumentException(
                $"Event {request.EventKey} needs recipients: name the users, or give the client and unit for a role-based event.",
                nameof(request));
        }

        return await resolver.ResolveAsync(request.EventKey, organizationId, request.OrgUnitPath, ct);
    }
}
