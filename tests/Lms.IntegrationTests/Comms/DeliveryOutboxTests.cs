using Lms.Api.Infrastructure;
using Lms.Domain.Comms;
using Lms.Domain.Identity;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Comms;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Comms;

/// <summary>M6 Task 5: the outbox kernel (write path, dispatcher, caps, quiet hours, retention) against the fake channel.</summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveryOutboxTests(LmsApiFactory factory)
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    /// <summary>A minute ahead, whole seconds (the database keeps six digits, an exact comparison needs none to lose).</summary>
    private static DateTime Soon()
    {
        var t = DateTime.UtcNow.AddMinutes(1);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private FakeDeliveryChannels Fakes => factory.Services.GetRequiredService<FakeDeliveryChannels>();

    private async Task<AccessTree> NewClientAsync(string name)
    {
        Fakes.Reset();
        return await AccessTestData.SeedAsync(factory, name);
    }

    private async Task<Guid> NewUserAsync(string? email = "")
    {
        var id = Guid.CreateVersion7();
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var user = new User { UserName = "u" + id.ToString("N"), Email = email == "" ? $"{id:N}@example.test" : email, Status = UserStatus.Active };
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, id);
        db.Add(user);
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SetAsync<T>(AccessTree tree, SettingKey<T> key, T value)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<ISettingsResolver>().SetAsync(key, tree.Root.Id, value, false);
    }

    private async Task PublishAsync(AccessTree tree, string eventKey, IReadOnlyList<Guid> users, bool urgent = false)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        var parameters = NotificationEvents.Find(eventKey)!.Parameters.ToDictionary(p => p, _ => "x");
        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(
            new NotificationRequest(eventKey, tree.Org.Id, users, parameters, Urgent: urgent));
    }

    private async Task DispatchAsync(DateTime now, Guid? onlyClient = null)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<DeliveryDispatchJob>().RunAtAsync(now, onlyClient, default);
    }

    private async Task<List<DeliveryOutbox>> RowsAsync(AccessTree tree, Guid? user = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<DeliveryOutbox>().AsNoTracking()
            .Where(r => r.OrganizationId == tree.Org.Id && (user == null || r.UserId == user))
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).ToListAsync();
    }

    private async Task UpdateRowsAsync(AccessTree tree, Action<DeliveryOutbox> change)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        foreach (var row in await db.Set<DeliveryOutbox>().Where(r => r.OrganizationId == tree.Org.Id).ToListAsync())
        {
            change(row);
        }

        await db.SaveChangesAsync();
    }

    // --- write path

    [Fact]
    public async Task Publish_writes_the_in_app_row_and_one_email_row_together()
    {
        var tree = await NewClientAsync("DO1");
        var user = await NewUserAsync();

        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);

        var row = Assert.Single(await RowsAsync(tree, user));
        Assert.Equal(NotificationChannels.Email, row.Channel);
        Assert.Equal(DeliveryStates.Pending, row.Status);
        Assert.NotNull(row.NotificationId);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.Set<Notification>().AnyAsync(n => n.Id == row.NotificationId && n.UserId == user));
    }

    [Fact]
    public async Task No_delivery_without_contact()
    {
        var tree = await NewClientAsync("DO2");
        var user = await NewUserAsync(email: null);

        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);

        Assert.Empty(await RowsAsync(tree, user));
    }

    [Fact]
    public async Task Client_setting_user_preference_and_catalog_default_decide_in_that_order()
    {
        var tree = await NewClientAsync("DO3");
        var user = await NewUserAsync();

        // Catalog default: an event with no channel list is in-app only.
        await PublishAsync(tree, NotificationEvents.MessagesNewMessage, [user]);
        Assert.Empty(await RowsAsync(tree, user));

        // A saved user choice turns the email on for it.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new NotificationPreference(user, NotificationEvents.MessagesNewMessage, NotificationChannels.Email, true));
            db.Add(new NotificationPreference(user, NotificationEvents.CommsCapReached, NotificationChannels.Email, false));
            await db.SaveChangesAsync();
        }

        await PublishAsync(tree, NotificationEvents.MessagesNewMessage, [user]);
        Assert.Single(await RowsAsync(tree, user));

        // A user who opted out gets none even where the catalog says email.
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        Assert.Single(await RowsAsync(tree, user));

        // The client switch beats everything.
        await SetAsync(tree, SettingKeys.CommsEmailEnabled, false);
        await PublishAsync(tree, NotificationEvents.MessagesNewMessage, [user]);
        Assert.Single(await RowsAsync(tree, user));
    }

    [Fact]
    public async Task A_mandatory_event_ignores_the_user_preference_but_not_the_client_switch()
    {
        var tree = await NewClientAsync("DO4");
        var user = await NewUserAsync();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new NotificationPreference(user, NotificationEvents.MessagingWarning, NotificationChannels.Email, false));
            await db.SaveChangesAsync();
        }

        await PublishAsync(tree, NotificationEvents.MessagingWarning, [user]);
        Assert.Single(await RowsAsync(tree, user));

        await SetAsync(tree, SettingKeys.CommsEmailEnabled, false);
        await PublishAsync(tree, NotificationEvents.MessagingWarning, [user]);
        Assert.Single(await RowsAsync(tree, user));
    }

    [Fact]
    public async Task There_is_one_row_per_notification_and_channel()
    {
        var tree = await NewClientAsync("DO5");
        var user = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        var first = Assert.Single(await RowsAsync(tree, user));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new DeliveryOutbox(tree.Org.Id, first.NotificationId, user, first.Channel, null, false, DateTime.UtcNow, null, TimeSpan.FromHours(1)));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_suppressed_address_is_kept_as_a_skipped_row_and_never_sent()
    {
        var tree = await NewClientAsync("DO6");
        var user = await NewUserAsync("blocked@example.test");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ContactSuppression(tree.Org.Id, NotificationChannels.Email, "Blocked@Example.test", SuppressionReasons.HardBounce, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        await DispatchAsync(DateTime.UtcNow.AddMinutes(1), tree.Org.Id);

        var row = Assert.Single(await RowsAsync(tree, user));
        Assert.Equal(DeliveryStates.Skipped, row.Status);
        Assert.Equal(DeliveryCodes.Suppressed, row.LastErrorCode);
        Assert.Empty(Fakes.Email.Sent);
    }

    // --- dispatcher

    [Fact]
    public async Task Dispatch_sends_through_the_channel_and_marks_the_row_sent()
    {
        var tree = await NewClientAsync("DO7");
        var user = await NewUserAsync("a@example.test");
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);

        await DispatchAsync(DateTime.UtcNow.AddMinutes(1), tree.Org.Id);

        var row = Assert.Single(await RowsAsync(tree, user));
        Assert.Equal(DeliveryStates.Sent, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.SentAtUtc);
        var sent = Assert.Single(Fakes.Email.Sent);
        Assert.Equal("a@example.test", sent.Address);
        Assert.Equal(NotificationEvents.CommsCapReached, sent.EventKey);
    }

    [Fact]
    public async Task A_row_that_is_not_due_yet_is_left_alone()
    {
        var tree = await NewClientAsync("DO8");
        var user = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        await UpdateRowsAsync(tree, r => r.Retry(DateTime.UtcNow.AddHours(1), null));

        await DispatchAsync(DateTime.UtcNow, tree.Org.Id);

        Assert.Empty(Fakes.Email.Sent);
    }

    [Fact]
    public async Task A_crash_during_Sending_redelivers_after_the_lease_and_not_before()
    {
        var tree = await NewClientAsync("DO9");
        var user = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        var now = Soon();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE DeliveryOutbox SET Status = 'Sending', Attempts = 1, LeaseUntilUtc = {now.AddMinutes(1)} WHERE OrganizationId = {tree.Org.Id}");
        }

        await DispatchAsync(now, tree.Org.Id);
        Assert.Empty(Fakes.Email.Sent); // the other worker may still be running

        await DispatchAsync(now.AddMinutes(3), tree.Org.Id);
        Assert.Single(Fakes.Email.Sent);
        Assert.Equal(DeliveryStates.Sent, Assert.Single(await RowsAsync(tree, user)).Status);
    }

    [Fact]
    public async Task Two_dispatchers_never_double_claim()
    {
        var tree = await NewClientAsync("DO10");
        var users = new List<Guid>();
        for (var i = 0; i < 40; i++)
        {
            users.Add(await NewUserAsync());
        }

        await PublishAsync(tree, NotificationEvents.CommsCapReached, users);
        Fakes.Email.Delay = TimeSpan.FromMilliseconds(5);
        var now = Soon();

        await Task.WhenAll(DispatchAsync(now, tree.Org.Id), DispatchAsync(now, tree.Org.Id), DispatchAsync(now, tree.Org.Id));

        Assert.Equal(40, Fakes.Email.Sent.Count);
        Assert.Equal(40, Fakes.Email.Sent.Select(s => s.OutboxId).Distinct().Count());
        Assert.All(await RowsAsync(tree), r => Assert.Equal((DeliveryStates.Sent, 1), (r.Status, r.Attempts)));
    }

    // --- retries

    [Fact]
    public async Task A_transient_failure_backs_off_exponentially_with_jitter_and_stops_after_the_attempt_limit()
    {
        var tree = await NewClientAsync("DO11");
        var user = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        var now = Soon();

        for (var attempt = 1; attempt <= 7; attempt++)
        {
            Fakes.Email.Enqueue(DeliveryOutcome.Transient("timeout"));
            await DispatchAsync(now, tree.Org.Id);
            var row = Assert.Single(await RowsAsync(tree, user));
            Assert.Equal(DeliveryStates.Pending, row.Status);
            Assert.Equal(attempt, row.Attempts);
            Assert.Equal("timeout", row.LastErrorCode);
            var seconds = Math.Min(30 * Math.Pow(2, attempt - 1), 3600);
            var wait = (row.NextAttemptAtUtc - now).TotalSeconds;
            Assert.InRange(wait, seconds * 0.8 - 1, Math.Min(seconds * 1.2, 3600) + 1);
            now = row.NextAttemptAtUtc.AddSeconds(1);
        }

        Fakes.Email.Enqueue(DeliveryOutcome.Transient("timeout"));
        await DispatchAsync(now, tree.Org.Id);

        var last = Assert.Single(await RowsAsync(tree, user));
        Assert.Equal((DeliveryStates.Failed, 8, DeliveryCodes.AttemptsExhausted), (last.Status, last.Attempts, last.LastErrorCode));
    }

    [Fact]
    public async Task A_longer_Retry_After_wins_over_the_backoff()
    {
        var tree = await NewClientAsync("DO12");
        var user = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        var now = Soon();

        Fakes.Email.Enqueue(DeliveryOutcome.Transient("rate_limited", TimeSpan.FromMinutes(10)));
        await DispatchAsync(now, tree.Org.Id);

        Assert.Equal(now.AddMinutes(10), Assert.Single(await RowsAsync(tree, user)).NextAttemptAtUtc);
    }

    [Fact]
    public async Task A_permanent_failure_ends_the_row_and_may_suppress_the_address()
    {
        var tree = await NewClientAsync("DO13");
        var user = await NewUserAsync("gone@example.test");
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        Fakes.Email.Enqueue(DeliveryOutcome.Permanent("invalid_recipient", suppress: true));

        await DispatchAsync(DateTime.UtcNow.AddMinutes(1), tree.Org.Id);

        var row = Assert.Single(await RowsAsync(tree, user));
        Assert.Equal((DeliveryStates.Failed, "invalid_recipient"), (row.Status, row.LastErrorCode));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var suppression = await db.Set<ContactSuppression>().SingleAsync(s => s.OrganizationId == tree.Org.Id);
        Assert.Equal(ContactSuppression.Hash("gone@example.test"), suppression.AddressHash);
        Assert.DoesNotContain("gone@", suppression.AddressHash);
    }

    [Fact]
    public async Task A_provider_that_is_not_configured_keeps_the_row_waiting_without_counting_an_attempt()
    {
        var tree = await NewClientAsync("DO14");
        var user = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        var now = Soon();
        Fakes.Email.Enqueue(DeliveryOutcome.NotConfigured());

        await DispatchAsync(now, tree.Org.Id);

        var row = Assert.Single(await RowsAsync(tree, user));
        Assert.Equal((DeliveryStates.Pending, 0, "not_configured"), (row.Status, row.Attempts, row.LastErrorCode));
        Assert.Equal(now + DeliveryPolicy.NotConfiguredWait, row.NextAttemptAtUtc);
    }

    [Fact]
    public async Task A_row_past_its_time_to_live_expires_instead_of_being_sent()
    {
        var tree = await NewClientAsync("DO15");
        var user = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);

        await DispatchAsync(DateTime.UtcNow.AddHours(25), tree.Org.Id);

        var row = Assert.Single(await RowsAsync(tree, user));
        Assert.Equal((DeliveryStates.Expired, DeliveryCodes.Expired), (row.Status, row.LastErrorCode));
        Assert.Empty(Fakes.Email.Sent);
    }

    // --- quiet hours and caps

    private async Task QuietAroundNowAsync(AccessTree tree)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Riyadh);
        await SetAsync(tree, SettingKeys.CommsQuietHoursEnabled, true);
        await SetAsync(tree, SettingKeys.CommsQuietHoursStart, local.AddHours(-1).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        await SetAsync(tree, SettingKeys.CommsQuietHoursEnd, local.AddHours(1).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Held_email_is_not_lost_by_quiet_hours()
    {
        var tree = await NewClientAsync("DO16");
        var user = await NewUserAsync();
        await QuietAroundNowAsync(tree);

        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user]);
        var held = Assert.Single(await RowsAsync(tree, user));
        Assert.NotNull(held.HeldUntilUtc);
        Assert.True(held.HeldUntilUtc > DateTime.UtcNow);
        // The time to live starts when the hold ends, so a long hold cannot expire the row.
        Assert.Equal(held.HeldUntilUtc!.Value + DeliveryPolicy.TimeToLive(NotificationChannels.Email), held.ExpiresAtUtc);

        await DispatchAsync(DateTime.UtcNow.AddMinutes(5), tree.Org.Id);
        Assert.Empty(Fakes.Email.Sent);

        await DispatchAsync(held.HeldUntilUtc.Value.AddMinutes(1), tree.Org.Id);
        Assert.Single(Fakes.Email.Sent);
    }

    [Fact]
    public async Task An_urgent_event_ignores_quiet_hours_and_caps()
    {
        var tree = await NewClientAsync("DO17");
        var user = await NewUserAsync();
        await QuietAroundNowAsync(tree);
        await SetAsync(tree, SettingKeys.CommsMaxEmailPerDay, 1);
        await SetAsync(tree, SettingKeys.CommsMaxEmailPerUserPerDay, 1);

        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user], urgent: true);
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [user], urgent: true);
        await DispatchAsync(DateTime.UtcNow.AddMinutes(1), tree.Org.Id);

        Assert.Equal(2, Fakes.Email.Sent.Count);
        Assert.All(await RowsAsync(tree, user), r => Assert.Null(r.HeldUntilUtc));
    }

    [Fact]
    public async Task A_mandatory_event_ignores_quiet_hours_and_caps()
    {
        var tree = await NewClientAsync("DO18");
        var user = await NewUserAsync();
        await QuietAroundNowAsync(tree);
        await SetAsync(tree, SettingKeys.CommsMaxEmailPerUserPerDay, 1);

        await PublishAsync(tree, NotificationEvents.MessagingWarning, [user]);
        await PublishAsync(tree, NotificationEvents.MessagingWarning, [user]);
        await DispatchAsync(DateTime.UtcNow.AddMinutes(1), tree.Org.Id);

        Assert.Equal(2, Fakes.Email.Sent.Count);
    }

    [Fact]
    public async Task Cap_overflow_is_sent_the_next_client_day()
    {
        var tree = await NewClientAsync("DO19");
        await SetAsync(tree, SettingKeys.CommsMaxEmailPerDay, 2);
        var users = new List<Guid> { await NewUserAsync(), await NewUserAsync(), await NewUserAsync() };
        await PublishAsync(tree, NotificationEvents.CommsCapReached, users);
        var now = Soon();

        await DispatchAsync(now, tree.Org.Id);

        Assert.Equal(2, Fakes.Email.Sent.Count);
        var overflow = Assert.Single(await RowsAsync(tree), r => r.Status == DeliveryStates.Pending);
        var nextDay = DeliveryPolicy.ClientDay(now, Riyadh).NextStartUtc;
        Assert.Equal(nextDay, overflow.HeldUntilUtc);

        await DispatchAsync(nextDay.AddMinutes(1), tree.Org.Id);
        Assert.Equal(3, Fakes.Email.Sent.Count);
    }

    [Fact]
    public async Task Per_recipient_daily_cap_holds_the_overflow()
    {
        var tree = await NewClientAsync("DO20");
        await SetAsync(tree, SettingKeys.CommsMaxEmailPerUserPerDay, 1);
        var chatty = await NewUserAsync();
        var quiet = await NewUserAsync();
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [chatty]);
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [chatty]);
        await PublishAsync(tree, NotificationEvents.CommsCapReached, [quiet]);
        var now = Soon();

        await DispatchAsync(now, tree.Org.Id);

        Assert.Equal(2, Fakes.Email.Sent.Count);
        Assert.Single(await RowsAsync(tree, quiet), r => r.Status == DeliveryStates.Sent);
        var chattyRows = await RowsAsync(tree, chatty);
        Assert.Equal(1, chattyRows.Count(r => r.Status == DeliveryStates.Sent));
        Assert.Equal(DeliveryPolicy.ClientDay(now, Riyadh).NextStartUtc, Assert.Single(chattyRows, r => r.Status == DeliveryStates.Pending).HeldUntilUtc);
    }

    // --- retention and registration

    [Fact]
    public async Task Retention_deletes_finished_rows_older_than_the_client_setting_and_keeps_the_rest()
    {
        var tree = await NewClientAsync("DO21");
        await SetAsync(tree, SettingKeys.CommsDeliveryRetentionDays, 30);
        var user = await NewUserAsync();
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            DeliveryOutbox Row(int ageDays, string status)
            {
                var row = new DeliveryOutbox(tree.Org.Id, Guid.NewGuid(), user, NotificationChannels.Email, null, false, now.AddDays(-ageDays), null, TimeSpan.FromHours(1));
                if (status == DeliveryStates.Sent)
                {
                    row.MarkSent(now.AddDays(-ageDays), "fake", null);
                }

                return row;
            }

            db.AddRange(Row(45, DeliveryStates.Sent), Row(10, DeliveryStates.Sent), Row(45, DeliveryStates.Pending));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
            await scope.ServiceProvider.GetRequiredService<DeliveryRetentionJob>().RunAtAsync(now, tree.Org.Id, default);
        }

        var left = await RowsAsync(tree, user);
        Assert.Equal(2, left.Count);
        Assert.DoesNotContain(left, r => r.Status == DeliveryStates.Sent && r.CreatedAtUtc < now.AddDays(-30));
    }

    [Fact]
    public void The_dispatcher_and_retention_recurring_jobs_are_registered_at_startup()
    {
        using var scope = factory.Services.CreateScope();
        using var connection = scope.ServiceProvider.GetRequiredService<Hangfire.JobStorage>().GetConnection();

        var jobs = Hangfire.Storage.StorageConnectionExtensions.GetRecurringJobs(connection);

        Assert.Contains(jobs, j => j.Id == DeliveryDispatchJob.RecurringJobId && j.Cron == DeliveryDispatchJob.Cron);
        Assert.Contains(jobs, j => j.Id == DeliveryRetentionJob.RecurringJobId && j.Cron == DeliveryRetentionJob.Cron);
    }

    // --- isolation

    [Fact]
    public async Task Delivery_rows_and_suppressions_of_client_B_are_invisible_in_client_A_context()
    {
        var a = await NewClientAsync("DO22A");
        var b = await AccessTestData.SeedAsync(factory, "DO22B");
        var userA = await NewUserAsync();
        var userB = await NewUserAsync();
        await PublishAsync(a, NotificationEvents.CommsCapReached, [userA]);
        await PublishAsync(b, NotificationEvents.CommsCapReached, [userB]);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ContactSuppression(b.Org.Id, NotificationChannels.Email, "x@example.test", SuppressionReasons.Complaint, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await using var asA = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        await using var noClient = factory.CreateDbContext(new TestCurrentContext { UserId = userA });

        Assert.Equal([userA], await asA.Set<DeliveryOutbox>().Select(r => r.UserId).ToListAsync());
        Assert.Empty(await asA.Set<ContactSuppression>().ToListAsync());
        Assert.Empty(await noClient.Set<DeliveryOutbox>().ToListAsync());
    }

    [Fact]
    public async Task One_clients_settings_never_affect_another_clients_delivery()
    {
        var a = await NewClientAsync("DO23A");
        var b = await AccessTestData.SeedAsync(factory, "DO23B");
        await SetAsync(a, SettingKeys.CommsEmailEnabled, false);
        var userA = await NewUserAsync();
        var userB = await NewUserAsync();

        await PublishAsync(a, NotificationEvents.CommsCapReached, [userA]);
        await PublishAsync(b, NotificationEvents.CommsCapReached, [userB]);

        Assert.Empty(await RowsAsync(a));
        Assert.Single(await RowsAsync(b));
    }
}
