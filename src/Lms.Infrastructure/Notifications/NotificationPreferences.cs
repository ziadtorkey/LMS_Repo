using Lms.Domain.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Notifications;

public sealed class NotificationPreferences(LmsDbContext db) : INotificationPreferences
{
    /// <summary>Default when the user saved nothing: in-app is on and locked, email on, push off.</summary>
    public static bool DefaultFor(string channel) =>
        channel is NotificationChannels.InApp or NotificationChannels.Email;

    public async Task<bool> IsEnabledAsync(
        Guid userId, string eventKey, string channel, CancellationToken cancellationToken = default)
    {
        if (channel == NotificationChannels.InApp)
        {
            return true; // cannot be switched off
        }

        var saved = await db.Set<NotificationPreference>()
            .Where(x => x.UserId == userId && x.EventKey == eventKey && x.Channel == channel)
            .Select(x => (bool?)x.Enabled)
            .SingleOrDefaultAsync(cancellationToken);
        return saved ?? DefaultFor(channel);
    }

    /// <summary>Every catalog event x channel for the user, saved values over defaults (in-app always on).</summary>
    public async Task<IReadOnlyList<NotificationPreferenceDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var saved = await db.Set<NotificationPreference>()
            .Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => (x.EventKey, x.Channel), x => x.Enabled, cancellationToken);

        return NotificationEvents.All
            .SelectMany(e => NotificationChannels.All.Select(channel => new NotificationPreferenceDto(
                e.Key,
                channel,
                channel == NotificationChannels.InApp || saved.GetValueOrDefault((e.Key, channel), DefaultFor(channel)),
                Locked: channel == NotificationChannels.InApp)))
            .ToList();
    }

    /// <summary>Replaces all saved rows of the user with the given ones, in one save.</summary>
    public async Task ReplaceAsync(
        Guid userId, IEnumerable<(string EventKey, string Channel, bool Enabled)> preferences, CancellationToken cancellationToken)
    {
        var existing = await db.Set<NotificationPreference>().Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        db.RemoveRange(existing);
        db.AddRange(preferences.Select(p => new NotificationPreference(userId, p.EventKey, p.Channel, p.Enabled)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
