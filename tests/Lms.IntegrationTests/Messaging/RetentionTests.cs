using Lms.Api.Infrastructure;
using Lms.Domain.AcademicYears;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Infrastructure.Messaging;
using Lms.Infrastructure.Moderation;
using Lms.IntegrationTests.AcademicYears;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Messaging;

/// <summary>Messaging retention jobs (M6 Task 16, D66, D71, L23): per client, the values come from settings, open reports hold.</summary>
[Collection(ApiCollection.Name)]
public sealed class RetentionTests(LmsApiFactory factory)
{
    private static readonly DateOnly Today = new(2026, 7, 1);
    private static readonly Guid Alice = Guid.Parse("0198a6f2-0000-7000-8000-0000000000a1");
    private static readonly Guid Bob = Guid.Parse("0198a6f2-0000-7000-8000-0000000000b2");

    private static DateTime At(DateOnly day, int addDays = 0) => day.AddDays(addDays).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);

    private async Task RunAsync<T>(Func<T, Task> run)
        where T : notnull
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true); // the system context, as the scheduler runs it
        await run(scope.ServiceProvider.GetRequiredService<T>());
    }

    private Task RunMessagesAsync() => RunAsync<MessageRetentionJob>(j => j.RunAsync(Today, CancellationToken.None));

    private async Task<(Conversation Conversation, List<Message> Messages)> AddMessagesAsync(Guid organizationId, params DateTime[] sentAt)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var conversation = Conversation.Direct(organizationId, Alice, Guid.NewGuid(), DateTime.UtcNow);
        db.Add(conversation);
        var messages = sentAt.Select((at, i) => new Message(conversation, Alice, $"text {i}", at)).ToList();
        db.AddRange(messages);
        await db.SaveChangesAsync();
        return (conversation, messages);
    }

    private async Task<AccessTree> ClientWithClosedYearAsync(string name, DateOnly end)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        await YearTestKit.SeedYearAsync(factory, tree.School1, end.AddYears(-1).AddDays(1), end, AcademicYearStatus.Closed);
        return tree;
    }

    private async Task<bool> ExistsAsync<T>(Guid id)
        where T : Lms.Domain.Common.Entity
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<T>().AnyAsync(e => e.Id == id);
    }

    [Fact]
    public async Task Messages_of_a_year_closed_more_than_a_year_ago_are_purged_with_attachments_and_only_in_their_own_client()
    {
        var a = await ClientWithClosedYearAsync("RA1", new DateOnly(2024, 6, 30));
        var b = await AccessTestData.SeedAsync(factory, "RA2"); // another client with no closed year: nothing of it may be purged
        var (conversation, old) = await AddMessagesAsync(a.Org.Id, At(new DateOnly(2024, 3, 1)), At(new DateOnly(2024, 6, 30)));
        var (_, recent) = await AddMessagesAsync(a.Org.Id, At(new DateOnly(2024, 9, 1)));
        var (_, other) = await AddMessagesAsync(b.Org.Id, At(new DateOnly(2020, 1, 1)));
        Guid attachmentId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var attachment = new MessageAttachment(conversation, Alice, Guid.NewGuid(), "old.pdf", "application/pdf", 10, At(new DateOnly(2024, 3, 1)));
            attachment.AttachTo(old[0]);
            db.Add(attachment);
            await db.SaveChangesAsync();
            attachmentId = attachment.Id;
        }

        await RunMessagesAsync();

        Assert.All(old, m => Assert.False(ExistsAsync<Message>(m.Id).Result));
        Assert.False(await ExistsAsync<MessageAttachment>(attachmentId));
        Assert.True(await ExistsAsync<Message>(recent[0].Id)); // sent after the closed year ended
        Assert.True(await ExistsAsync<Message>(other[0].Id)); // the other client has no closed year
    }

    [Fact]
    public async Task A_year_closed_less_than_the_window_ago_keeps_its_messages()
    {
        var a = await ClientWithClosedYearAsync("RA3", new DateOnly(2026, 1, 31));
        var (_, messages) = await AddMessagesAsync(a.Org.Id, At(new DateOnly(2025, 10, 1)));

        await RunMessagesAsync();

        Assert.True(await ExistsAsync<Message>(messages[0].Id));
    }

    [Fact]
    public async Task Open_reports_hold_their_message_and_context_past_the_window()
    {
        var a = await ClientWithClosedYearAsync("RA4", new DateOnly(2024, 6, 30));
        var (conversation, messages) = await AddMessagesAsync(a.Org.Id, At(new DateOnly(2024, 2, 1)), At(new DateOnly(2024, 2, 2)));
        Guid openReport;
        Guid dismissedReport;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var open = new MessageReport(a.Org.Id, messages[0].Id, conversation.Id, Bob, Alice, "Spam", null, "[{\"x\":1}]", null);
            var dismissed = new MessageReport(a.Org.Id, messages[1].Id, conversation.Id, Bob, Alice, "Spam", null, "[{\"x\":1}]", null);
            dismissed.Dismiss(Guid.NewGuid(), At(Today, -400));
            db.AddRange(open, dismissed);
            await db.SaveChangesAsync();
            (openReport, dismissedReport) = (open.Id, dismissed.Id);
        }

        await RunMessagesAsync();
        await RunAsync<ReportContextRetentionJob>(j => j.RunAsync(Today, CancellationToken.None));

        Assert.True(await ExistsAsync<Message>(messages[0].Id)); // an open report holds its message
        Assert.False(await ExistsAsync<Message>(messages[1].Id));
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal("[{\"x\":1}]", (await check.Set<MessageReport>().SingleAsync(r => r.Id == openReport)).ContextJson); // and its context, however old
        Assert.Equal(MessageReport.PurgedContext, (await check.Set<MessageReport>().SingleAsync(r => r.Id == dismissedReport)).ContextJson);
    }

    [Fact]
    public async Task Report_context_is_purged_180_days_after_resolution_and_not_before()
    {
        var a = await AccessTestData.SeedAsync(factory, "RA5");
        var (conversation, messages) = await AddMessagesAsync(a.Org.Id, At(Today, -300), At(Today, -300));
        Guid oldId;
        Guid freshId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var oldReport = new MessageReport(a.Org.Id, messages[0].Id, conversation.Id, Bob, Alice, "Spam", null, "[{\"x\":1}]", null);
            oldReport.Dismiss(Guid.NewGuid(), At(Today, -200));
            var freshReport = new MessageReport(a.Org.Id, messages[1].Id, conversation.Id, Bob, Alice, "Spam", null, "[{\"x\":1}]", null);
            freshReport.Dismiss(Guid.NewGuid(), At(Today, -10));
            db.AddRange(oldReport, freshReport);
            await db.SaveChangesAsync();
            (oldId, freshId) = (oldReport.Id, freshReport.Id);
        }

        await RunAsync<ReportContextRetentionJob>(j => j.RunAsync(Today, CancellationToken.None));

        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(MessageReport.PurgedContext, (await check.Set<MessageReport>().SingleAsync(r => r.Id == oldId)).ContextJson);
        Assert.NotEqual(MessageReport.PurgedContext, (await check.Set<MessageReport>().SingleAsync(r => r.Id == freshId)).ContextJson);
    }

    [Fact]
    public async Task Removed_original_text_is_purged_after_180_days_and_the_tombstone_stays()
    {
        var a = await AccessTestData.SeedAsync(factory, "RA6");
        var (conversation, messages) = await AddMessagesAsync(a.Org.Id, At(Today, -300), At(Today, -300));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Set<Message>().Where(m => m.ConversationId == conversation.Id).ToListAsync();
            var removedOld = new MessageReport(a.Org.Id, messages[0].Id, conversation.Id, Bob, Alice, "Spam", null, "[]", null);
            removedOld.TakeAction(ModerationActions.Remove, Guid.NewGuid(), At(Today, -200));
            var removedFresh = new MessageReport(a.Org.Id, messages[1].Id, conversation.Id, Bob, Alice, "Spam", null, "[]", null);
            removedFresh.TakeAction(ModerationActions.Remove, Guid.NewGuid(), At(Today, -10));
            tracked.ForEach(m => m.RemoveByModerator());
            db.AddRange(removedOld, removedFresh);
            await db.SaveChangesAsync();
        }

        await RunAsync<RemovedOriginalRetentionJob>(j => j.RunAsync(Today, CancellationToken.None));

        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var purged = await check.Set<Message>().SingleAsync(m => m.Id == messages[0].Id);
        Assert.Equal((string.Empty, Message.RemovedState), (purged.Body, purged.State));
        Assert.NotEqual(string.Empty, (await check.Set<Message>().SingleAsync(m => m.Id == messages[1].Id)).Body); // still inside the window
    }

    [Fact]
    public async Task Retention_values_come_from_settings()
    {
        var a = await ClientWithClosedYearAsync("RA7", new DateOnly(2024, 6, 30));
        var (conversation, messages) = await AddMessagesAsync(a.Org.Id, At(new DateOnly(2024, 3, 1)), At(Today, -300));
        Guid reportId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var report = new MessageReport(a.Org.Id, messages[1].Id, conversation.Id, Bob, Alice, "Spam", null, "[{\"x\":1}]", null);
            report.Dismiss(Guid.NewGuid(), At(Today, -200));
            db.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(SettingKeys.MessagingRetentionYearsAfterClose, a.Root.Id, 3, false); // the legal answer changes data, not code
            await scope.Settings.SetAsync(SettingKeys.ModerationReportContextRetentionDays, a.Root.Id, 400, false);
        }

        await RunMessagesAsync();
        await RunAsync<ReportContextRetentionJob>(j => j.RunAsync(Today, CancellationToken.None));

        Assert.True(await ExistsAsync<Message>(messages[0].Id)); // 3 years after close: not yet
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal("[{\"x\":1}]", (await check.Set<MessageReport>().SingleAsync(r => r.Id == reportId)).ContextJson); // 400 days: not yet
    }

    [Fact]
    public async Task Pending_uploads_nobody_attached_are_purged_after_24_hours()
    {
        var a = await AccessTestData.SeedAsync(factory, "RA8");
        var (conversation, messages) = await AddMessagesAsync(a.Org.Id, DateTime.UtcNow);
        Guid stale;
        Guid fresh;
        Guid attached;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var staleRow = new MessageAttachment(conversation, Alice, Guid.NewGuid(), "stale.pdf", "application/pdf", 10, DateTime.UtcNow.AddHours(-25));
            var freshRow = new MessageAttachment(conversation, Alice, Guid.NewGuid(), "fresh.pdf", "application/pdf", 10, DateTime.UtcNow.AddHours(-1));
            var attachedRow = new MessageAttachment(conversation, Alice, Guid.NewGuid(), "kept.pdf", "application/pdf", 10, DateTime.UtcNow.AddHours(-30));
            attachedRow.AttachTo(messages[0]);
            db.AddRange(staleRow, freshRow, attachedRow);
            await db.SaveChangesAsync();
            (stale, fresh, attached) = (staleRow.Id, freshRow.Id, attachedRow.Id);
        }

        await RunMessagesAsync();

        Assert.False(await ExistsAsync<MessageAttachment>(stale));
        Assert.True(await ExistsAsync<MessageAttachment>(fresh));
        Assert.True(await ExistsAsync<MessageAttachment>(attached)); // attached files belong to their message
    }
}
