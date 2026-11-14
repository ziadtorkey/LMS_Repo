using Lms.Domain.Abstractions;
using Lms.Domain.Comms;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Comms;

/// <summary>
/// The delivery dispatcher (M6 Task 5, proposal #613 section 4.2): a recurring system job (platform context) that works through the clients one at a
/// time. Per client it returns the rows of crashed workers to <c>Pending</c> (lease over), expires rows past their time to live, then claims due
/// rows per channel with a <b>conditional update</b> (<c>Pending</c> to <c>Sending</c> with a lease), so any number of dispatchers, on any number of nodes,
/// never take the same row. Each claimed row is checked (contact, suppression, quiet hours, caps) and sent through its <see cref="IDeliveryChannel"/>;
/// the typed outcome decides retry (exponential back-off with jitter), end, or wait. It reads no content of other modules: the notification row for the
/// event key, parameters and link, and the user's address through <see cref="IContactResolver"/>.
/// </summary>
public sealed class DeliveryDispatchJob(
    LmsDbContext db, CommsClientSettings settings, IContactResolver contacts, IEnumerable<IDeliveryChannel> channels, IClock clock,
    IConfiguration configuration, ILogger<DeliveryDispatchJob> logger)
{
    public const string RecurringJobId = "comms-dispatch";

    /// <summary>Every minute (Hangfire's finest cron resolution); a publish is delivered within the minute.</summary>
    public const string Cron = "* * * * *";

    private const int Batch = 100;
    private const int MaxPerClientPerRun = 5000;

    private readonly Dictionary<string, IDeliveryChannel> _channels = channels.ToDictionary(c => c.Channel);

    /// <summary>The recurring entry point. <c>Comms:DispatcherEnabled</c> false (the integration host) turns the schedule into a no-op.</summary>
    public Task RunAsync(CancellationToken ct) =>
        configuration.GetValue("Comms:DispatcherEnabled", true) ? RunAtAsync(clock.UtcNow, null, ct) : Task.CompletedTask;

    /// <param name="only">One client only (an operator re-running a client, and tests that share a database with other clients).</param>
    public async Task RunAtAsync(DateTime now, Guid? only, CancellationToken ct)
    {
        var organizationIds = await db.Set<Organization>().Where(o => only == null || o.Id == only).Select(o => o.Id).ToListAsync(ct);
        foreach (var organizationId in organizationIds)
        {
            try
            {
                await RunClientAsync(organizationId, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One client's failure must not stop the others; its rows are picked up again next run.
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Delivery dispatch failed for client {OrganizationId}", organizationId);
            }
        }
    }

    private async Task RunClientAsync(Guid organizationId, DateTime now, CancellationToken ct)
    {
        var rows = db.Set<DeliveryOutbox>().Where(r => r.OrganizationId == organizationId);

        // A worker that died leaves a lease that runs out; the row goes back in the queue (its attempt stays counted).
        await rows.Where(r => r.Status == DeliveryStates.Sending && r.LeaseUntilUtc < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, DeliveryStates.Pending)
                .SetProperty(r => r.NextAttemptAtUtc, now)
                .SetProperty(r => r.LeaseUntilUtc, (DateTime?)null)
                .SetProperty(r => r.LastErrorCode, "lease_expired"), ct);
        await rows.Where(r => r.Status == DeliveryStates.Pending && r.ExpiresAtUtc < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, DeliveryStates.Expired)
                .SetProperty(r => r.LastErrorCode, DeliveryCodes.Expired), ct);

        CommsSettings? client = null;
        var processed = 0;
        foreach (var channel in _channels.Keys)
        {
            while (processed < MaxPerClientPerRun)
            {
                var due = await rows
                    .Where(r => r.Channel == channel && r.Status == DeliveryStates.Pending && r.NextAttemptAtUtc <= now)
                    .OrderBy(r => r.NextAttemptAtUtc).ThenBy(r => r.Id)
                    .Select(r => r.Id).Take(Batch).ToListAsync(ct);
                if (due.Count == 0)
                {
                    break;
                }

                client ??= await settings.GetAsync(organizationId, ct);
                var claimed = 0;
                foreach (var id in due)
                {
                    if (await ClaimAsync(id, now, ct))
                    {
                        claimed++;
                        await SendOneAsync(id, organizationId, client, now, ct);
                    }
                }

                processed += due.Count;
                if (claimed == 0)
                {
                    break; // other workers hold the whole batch
                }
            }
        }
    }

    /// <summary>One conditional update: only the worker that moves the row out of <c>Pending</c> owns it.</summary>
    private async Task<bool> ClaimAsync(Guid id, DateTime now, CancellationToken ct) =>
        await db.Set<DeliveryOutbox>()
            .Where(r => r.Id == id && r.Status == DeliveryStates.Pending && r.NextAttemptAtUtc <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, DeliveryStates.Sending)
                .SetProperty(r => r.LeaseUntilUtc, now + DeliveryPolicy.Lease)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1), ct) == 1;

    private async Task SendOneAsync(Guid id, Guid organizationId, CommsSettings client, DateTime now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var row = await db.Set<DeliveryOutbox>().FirstAsync(r => r.Id == id, ct);
        try
        {
            await DecideAsync(row, organizationId, client, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never leave a row in Sending behind a bug: it is retried with a back-off like any transient failure.
            logger.LogError(ex, "Delivery {DeliveryId} failed before it could be sent", id);
            db.ChangeTracker.Clear();
            row = await db.Set<DeliveryOutbox>().FirstAsync(r => r.Id == id, ct);
            Retry(row, now, "dispatch_error", null);
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task DecideAsync(DeliveryOutbox row, Guid organizationId, CommsSettings client, DateTime now, CancellationToken ct)
    {
        var addresses = await contacts.ResolveAsync([row.UserId], row.Channel, ct);
        if (!addresses.TryGetValue(row.UserId, out var address))
        {
            row.Skip(DeliveryCodes.NoContact);
            return;
        }

        var hash = ContactSuppression.Hash(address);
        if (await db.Set<ContactSuppression>().AnyAsync(s => s.OrganizationId == organizationId && s.Channel == row.Channel && s.AddressHash == hash, ct))
        {
            row.Skip(DeliveryCodes.Suppressed);
            return;
        }

        if (!row.IsPriority && await HoldReasonAsync(row, organizationId, client, now, ct) is { } until)
        {
            row.Hold(until, DeliveryPolicy.TimeToLive(row.Channel));
            return;
        }

        Notification? notification = null;
        if (row.NotificationId is { } notificationId)
        {
            notification = await db.Set<Notification>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == notificationId, ct);
            if (notification is null)
            {
                row.Skip("notification_gone");
                return;
            }
        }

        if (row.Attempts > DeliveryPolicy.MaxAttempts(row.Channel))
        {
            row.Fail(DeliveryCodes.AttemptsExhausted, _channels[row.Channel].ProviderName);
            return;
        }

        var channel = _channels[row.Channel];
        DeliveryOutcome outcome;
        try
        {
            outcome = await channel.SendAsync(
                new DeliveryRequest(row.Id, organizationId, row.UserId, address, notification?.EventKey, notification?.ParametersJson, notification?.DeepLink, row.TemplateKey),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Channel {Channel} threw for delivery {DeliveryId}", row.Channel, row.Id);
            outcome = DeliveryOutcome.Transient("channel_exception");
        }

        switch (outcome.Kind)
        {
            case DeliveryOutcomeKind.Accepted:
                row.MarkSent(now, channel.ProviderName, outcome.ProviderMessageId);
                break;
            case DeliveryOutcomeKind.NotConfigured:
                row.WaitForProvider(now + DeliveryPolicy.NotConfiguredWait);
                break;
            case DeliveryOutcomeKind.Permanent:
                row.Fail(outcome.ErrorCode ?? "rejected", channel.ProviderName);
                if (outcome.SuppressAddress)
                {
                    db.Add(new ContactSuppression(organizationId, row.Channel, address, SuppressionReasons.InvalidAddress, now));
                }

                break;
            default:
                Retry(row, now, outcome.ErrorCode ?? "transient", outcome.RetryAfter);
                break;
        }
    }

    private static void Retry(DeliveryOutbox row, DateTime now, string code, TimeSpan? retryAfter)
    {
        if (row.Attempts >= DeliveryPolicy.MaxAttempts(row.Channel))
        {
            row.Fail(DeliveryCodes.AttemptsExhausted, row.Provider ?? "none");
            return;
        }

        var jitter = Random.Shared.NextDouble() * 2 - 1;
        row.Retry(now + DeliveryPolicy.Backoff(row.Attempts, jitter, retryAfter), code);
    }

    /// <summary>When a non-priority row must wait: quiet hours, the client's daily cap, or the recipient's; null when it may go now.</summary>
    private async Task<DateTime?> HoldReasonAsync(DeliveryOutbox row, Guid organizationId, CommsSettings client, DateTime now, CancellationToken ct)
    {
        if (client.QuietUntil(now) is { } quietEnds)
        {
            return quietEnds;
        }

        var (dayStart, nextDay) = DeliveryPolicy.ClientDay(now, client.Zone);
        var sent = db.Set<DeliveryOutbox>()
            .Where(r => r.OrganizationId == organizationId && r.Channel == row.Channel && r.Status == DeliveryStates.Sent && r.SentAtUtc >= dayStart);
        if (await sent.CountAsync(ct) >= client.ClientCap(row.Channel))
        {
            return nextDay;
        }

        return await sent.CountAsync(r => r.UserId == row.UserId, ct) >= client.UserCap(row.Channel) ? nextDay : null;
    }
}
