using Lms.Domain.Comms;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Comms.Preferences;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Comms;

/// <summary>
/// Decides which outbox rows a published notification earns (proposal #613 section 4.1). For each non-in-app channel, in order: the client's channel
/// switch; the user's saved choice (never for a mandatory event), else the catalog default (an event with no channel list is in-app only); a contact
/// exists (otherwise no row); the address is not suppressed (a <c>Skipped</c> row is kept for the log); quiet hours hold the row until they end unless the
/// event is urgent or mandatory. The caller adds the rows to the same unit of work as the notification rows.
/// </summary>
public sealed class DeliveryPlanner(LmsDbContext db, CommsClientSettings settings, IContactResolver contacts)
{
    public async Task<IReadOnlyList<DeliveryOutbox>> PlanAsync(
        Guid organizationId, NotificationEventDefinition definition, IReadOnlyList<(Guid UserId, Guid NotificationId)> targets,
        bool urgent, DateTime now, CancellationToken ct)
    {
        var rows = new List<DeliveryOutbox>();
        if (targets.Count == 0)
        {
            return rows;
        }

        var client = await settings.GetAsync(organizationId, ct);
        var userIds = targets.Select(t => t.UserId).Distinct().ToList();
        var saved = await db.Set<NotificationPreference>().AsNoTracking()
            .Where(p => p.EventKey == definition.Key && userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => (p.UserId, p.Channel), p => p.Enabled, ct);
        var priority = urgent || definition.Mandatory;
        var heldUntil = priority ? null : client.QuietUntil(now);

        foreach (var channel in NotificationChannels.All.Where(c => c != NotificationChannels.InApp))
        {
            if (!client.IsEnabled(channel))
            {
                continue;
            }

            var addresses = await contacts.ResolveAsync(targets.Select(t => t.UserId).Distinct().ToList(), channel, ct);
            var suppressed = await SuppressedAsync(organizationId, channel, addresses.Values, ct);
            foreach (var (userId, notificationId) in targets)
            {
                // The same function as the preferences page (L10): event default and user choice and client switch and a usable contact.
                var choice = saved.TryGetValue((userId, channel), out var enabled) ? enabled : (bool?)null;
                var effective = EffectivePreferences.Resolve(new PreferenceInputs(definition, channel, choice, true, addresses.ContainsKey(userId)));
                if (!effective.Enabled || !addresses.TryGetValue(userId, out var address))
                {
                    continue; // switched off, or no contact: no row
                }

                var row = new DeliveryOutbox(
                    organizationId, notificationId, userId, channel, null, priority, now, heldUntil, DeliveryPolicy.TimeToLive(channel));
                if (suppressed.Contains(ContactSuppression.Hash(address)))
                {
                    row.Skip(DeliveryCodes.Suppressed);
                }

                rows.Add(row);
            }
        }

        return rows;
    }

    private async Task<HashSet<string>> SuppressedAsync(Guid organizationId, string channel, IEnumerable<string> addresses, CancellationToken ct)
    {
        var hashes = addresses.Select(ContactSuppression.Hash).Distinct().ToList();
        var found = new HashSet<string>();
        foreach (var chunk in hashes.Chunk(500))
        {
            found.UnionWith(await db.Set<ContactSuppression>().AsNoTracking()
                .Where(s => s.OrganizationId == organizationId && s.Channel == channel && chunk.Contains(s.AddressHash))
                .Select(s => s.AddressHash)
                .ToListAsync(ct));
        }

        return found;
    }
}
