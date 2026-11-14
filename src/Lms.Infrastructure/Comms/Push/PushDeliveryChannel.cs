using Lms.Domain.Abstractions;
using Lms.Domain.Comms;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Comms.Push;

/// <summary>
/// The push <see cref="IDeliveryChannel"/> (M6 Task 8): one outbox row is sent to every subscription of the user in the client. The text is generic and
/// bilingual (the notification's text is private, L9; the service worker may localise from <c>data.eventKey</c>). A subscription the push service reports gone
/// is deleted; the row is accepted when at least one subscription took it, retried when a subscription failed transiently, and fails permanently when none is left.
/// </summary>
public sealed class PushDeliveryChannel(LmsDbContext db, IPushProvider provider, PushKeyProtector protector, IClock clock) : IDeliveryChannel
{
    private const string Title = "LMS";
    private const string Body = "لديك إشعار جديد · You have a new notification";

    public string Channel => NotificationChannels.Push;

    public string ProviderName => provider.ProviderName;

    public async Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken ct)
    {
        var subscriptions = await db.Set<Domain.Comms.PushSubscription>()
            .Where(s => s.OrganizationId == request.OrganizationId && s.UserId == request.UserId)
            .ToListAsync(ct);
        if (subscriptions.Count == 0)
        {
            return DeliveryOutcome.Permanent("no_subscription");
        }

        var payload = PushPayloadBuilder.Build(request.EventKey ?? request.TemplateKey ?? "Notification", request.OutboxId, Title, Body, request.DeepLink);
        var accepted = 0;
        var notConfigured = false;
        PushResult? transient = null;
        string? permanent = null;
        foreach (var subscription in subscriptions)
        {
            var (p256dh, auth) = (protector.Unprotect(subscription.ProtectedP256dh), protector.Unprotect(subscription.ProtectedAuth));
            if (p256dh is null || auth is null)
            {
                db.Remove(subscription); // its keys cannot be read any more: unusable
                continue;
            }

            var result = await provider.SendAsync(new PushTarget(subscription.Endpoint, p256dh, auth), payload, DeliveryPolicy.TimeToLive(Channel), urgent: false, ct);
            switch (result.Kind)
            {
                case PushResultKind.Accepted:
                    accepted++;
                    subscription.MarkSuccess(clock.UtcNow);
                    break;
                case PushResultKind.Gone:
                    db.Remove(subscription);
                    break;
                case PushResultKind.NotConfigured:
                    notConfigured = true;
                    break;
                case PushResultKind.Transient:
                    transient = transient is null || result.RetryAfter > transient.RetryAfter ? result : transient;
                    break;
                default:
                    permanent = result.ErrorCode;
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        if (accepted > 0)
        {
            return DeliveryOutcome.Accepted();
        }

        if (notConfigured)
        {
            return DeliveryOutcome.NotConfigured();
        }

        if (transient is not null)
        {
            return DeliveryOutcome.Transient(transient.ErrorCode ?? "push_transient", transient.RetryAfter);
        }

        return DeliveryOutcome.Permanent(permanent ?? "no_subscription");
    }
}
