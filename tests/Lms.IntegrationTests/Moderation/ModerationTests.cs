using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Moderation;

/// <summary>
/// Reporting and moderating messages (M6 Task 15, FR-MSG-04, D66, D69, D70, L6, L16): the frozen context, the scope of a report by its units, the actions and their effects, the audit
/// of every read, what the reporter, the sender and a moderator can and cannot learn.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ModerationTests(LmsApiFactory factory)
{
    private sealed class World
    {
        public required AccessTree Tree { get; init; }
        public required RoleAssignment Reporter { get; init; }
        public required RoleAssignment Sender { get; init; }
        public required RoleAssignment RootAdmin { get; init; }
        public required RoleAssignment School1Admin { get; init; }
        public required RoleAssignment School2Admin { get; init; }
        public required RoleAssignment Manager { get; init; }
        public required Guid ConversationId { get; init; }
        public required List<Guid> MessageIds { get; init; }
    }

    private async Task<World> SetUpAsync(string name, bool senderAlsoAtSchool2 = false, bool senderHasNoUnit = false, int messageCount = 13)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(FeatureFlags.Messaging, tree.Root.Id, true, false);
        }

        var reporterUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var senderUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var reporter = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, reporterUser);
        var sender = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, senderUser);
        if (senderAlsoAtSchool2)
        {
            await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Teacher, senderUser);
        }

        if (senderHasNoUnit)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var assignment = await db.Set<RoleAssignment>().SingleAsync(a => a.Id == sender.Id);
            db.Remove(assignment); // a sender who has no active unit any more: the report must still be visible, at the root
            await db.SaveChangesAsync();
        }

        Guid conversationId;
        var ids = new List<Guid>();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var start = DateTime.UtcNow.AddHours(-3);
            var conversation = Conversation.Direct(tree.Org.Id, reporterUser, senderUser, start);
            db.Add(conversation);
            db.Add(new ConversationParticipant(conversation, reporterUser, start));
            db.Add(new ConversationParticipant(conversation, senderUser, start));
            for (var i = 0; i < messageCount; i++)
            {
                var message = new Message(conversation, i % 2 == 0 ? senderUser : reporterUser, $"message {i}", start.AddMinutes(i));
                db.Add(message);
                ids.Add(message.Id);
            }

            await db.SaveChangesAsync();
            conversationId = conversation.Id;
        }

        return new World
        {
            Tree = tree, Reporter = reporter, Sender = sender, ConversationId = conversationId, MessageIds = ids,
            RootAdmin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id),
            School1Admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id),
            School2Admin = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id),
            Manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, (await PeopleTestKit.AddUserAsync(factory)).Id),
        };
    }

    private Task<HttpResponseMessage> ReportAsync(World w, int messageIndex, string reason = "Harassment", string? note = null, RoleAssignment? who = null) =>
        SendAsync(factory, HttpMethod.Post, Routes.MessageReport(w.MessageIds[messageIndex]), who ?? w.Reporter, new ReportMessageRequest(reason, note));

    private async Task<Guid> ReportIdAsync(World w, int messageIndex)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<MessageReport>().SingleAsync(r => r.MessageId == w.MessageIds[messageIndex])).Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Web))!;

    private async Task<List<MessageReportListItemDto>> QueueAsync(RoleAssignment who, string query = "")
    {
        var response = await SendAsync(factory, HttpMethod.Get, Routes.ModerationReports + query, who);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<PagedResult<MessageReportListItemDto>>(response)).Items.ToList();
    }

    [Fact]
    public async Task Report_freezes_five_before_and_after_and_survives_the_sender_deleting_the_message()
    {
        var w = await SetUpAsync("MD1");
        var created = await ReportAsync(w, 6, ReportReasons.Harassment, "rude");
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        var id = await ReportIdAsync(w, 6);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Message>().SingleAsync(m => m.Id == w.MessageIds[6])).DeleteForEveryone(DateTime.UtcNow); // the sender deletes it afterwards
            await db.SaveChangesAsync();
        }

        var detail = await ReadAsync<MessageReportDto>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationReport(id), w.RootAdmin));

        Assert.Equal(11, detail.Context.Count); // the reported message, five before and five after
        Assert.Equal("message 6", detail.Context.Single(c => c.IsReported).Body); // the frozen text survives the delete
        Assert.Equal(Enumerable.Range(1, 11).Select(i => $"message {i}"), detail.Context.Select(c => c.Body));
        Assert.Equal((ReportReasons.Harassment, "rude"), (detail.Reason, detail.Note));
        Assert.Equal(ReportStatuses.InReview, detail.Status); // opening it moved it
    }

    [Fact]
    public async Task Context_is_cut_at_the_ends_of_the_conversation()
    {
        var w = await SetUpAsync("MD2");

        await ReportAsync(w, 2);
        var detail = await ReadAsync<MessageReportDto>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationReport(await ReportIdAsync(w, 2)), w.RootAdmin));

        Assert.Equal(8, detail.Context.Count); // two messages before it, five after
    }

    [Fact]
    public async Task Reporting_rules_own_message_stranger_duplicate_reason_note_and_the_daily_cap()
    {
        var w = await SetUpAsync("MD3");
        var stranger = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Student, (await PeopleTestKit.AddUserAsync(factory)).Id);

        var own = await ReportAsync(w, 7); // message 7 was sent by the reporter
        var asSender = await ReportAsync(w, 6, who: w.Sender);
        var outsider = await ReportAsync(w, 6, who: stranger);
        var badReason = await ReportAsync(w, 6, "Boring");
        var longNote = await ReportAsync(w, 6, note: new string('x', MessagingLimits.ReportNoteMaxLength + 1));
        var first = await ReportAsync(w, 6);
        var again = await ReportAsync(w, 6);

        Assert.Equal(HttpStatusCode.BadRequest, own.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, asSender.StatusCode); // the sender of message 6 is the sender: they cannot report their own
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode); // not a participant: nothing tells the message exists
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (badReason.StatusCode, longNote.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ProblemCodes.report_duplicate, (await ReadAsync<JsonElement>(again)).GetProperty("code").GetString());

        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(SettingKeys.ModerationMaxReportsPerUserPerDay, w.Tree.Root.Id, 1, false);
        }

        var capped = await ReportAsync(w, 4);
        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode); // one a day under the client setting
    }

    [Fact]
    public async Task A_report_is_in_the_queue_of_admins_at_or_above_its_unit_only_and_never_of_a_manager_a_teacher_or_another_client()
    {
        var w = await SetUpAsync("MD4");
        var other = await AccessTestData.SeedAsync(factory, "MD4B");
        var otherAdmin = await AccessTestData.AssignAsync(factory, other, other.Root, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id);
        await ReportAsync(w, 6);
        var id = await ReportIdAsync(w, 6);

        Assert.Single(await QueueAsync(w.RootAdmin));
        Assert.Single(await QueueAsync(w.School1Admin)); // the sender sits at School 1
        Assert.Empty(await QueueAsync(w.School2Admin)); // a sibling unit never sees it
        Assert.Empty(await QueueAsync(otherAdmin)); // nor another client
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.ModerationReport(id), w.School2Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.ModerationReport(id), otherAdmin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.ModerationReports, w.Manager)).StatusCode); // D70: a Manager has no moderation access
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.ModerationReports, w.Sender)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.ModerationReports, w.Reporter)).StatusCode);
        var zeros = await ReadAsync<ModerationStatsDto>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationStats, w.School2Admin));
        Assert.Equal((0, 0, 0), (zeros.Open, zeros.NewToday, zeros.Restricted)); // outside the scope the numbers are zeros, never 404
        Assert.Equal(1, (await ReadAsync<ModerationStatsDto>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationStats, w.RootAdmin))).Open);
    }

    [Fact]
    public async Task A_report_is_visible_for_a_sender_with_two_units_and_falls_back_to_the_root_with_none()
    {
        var two = await SetUpAsync("MD5", senderAlsoAtSchool2: true);
        var madeTwo = await ReportAsync(two, 6);
        Assert.True(madeTwo.StatusCode == HttpStatusCode.NoContent, await madeTwo.Content.ReadAsStringAsync());
        Assert.Single(await QueueAsync(two.School1Admin));
        Assert.Single(await QueueAsync(two.School2Admin)); // an admin at or above ANY unit of the sender

        var none = await SetUpAsync("MD6", senderHasNoUnit: true);
        var made = await ReportAsync(none, 6);
        Assert.True(made.StatusCode == HttpStatusCode.NoContent, await made.Content.ReadAsStringAsync());
        Assert.Single(await QueueAsync(none.RootAdmin)); // never invisible: the client root
        Assert.Empty(await QueueAsync(none.School1Admin));
    }

    [Fact]
    public async Task Every_moderator_read_is_audited_and_the_moderator_reads_only_the_reported_context_never_the_reporter()
    {
        var w = await SetUpAsync("MD7");
        await ReportAsync(w, 6, note: "private words");
        var id = await ReportIdAsync(w, 6);

        var response = await SendAsync(factory, HttpMethod.Get, Routes.ModerationReport(id), w.RootAdmin);
        var raw = await response.Content.ReadAsStringAsync();
        await SendAsync(factory, HttpMethod.Get, Routes.ModerationReport(id), w.RootAdmin);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var reads = await db.Set<AuditEntry>().Where(a => a.EntityType == "ModerationRead" && a.EntityId == id).ToListAsync();
        Assert.Equal(2, reads.Count); // each detail read is one audit row
        Assert.All(reads, r => Assert.DoesNotContain("message 6", r.AfterJson ?? string.Empty)); // no text in the audit log
        var reporter = w.Reporter.UserId.ToString();
        Assert.DoesNotContain(reporter, raw, StringComparison.OrdinalIgnoreCase); // the report never names the reporter
        Assert.DoesNotContain("message 12", raw); // only five after the reported message (index 11), nothing beyond
        var participantOnlyRoute = await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(w.ConversationId), w.RootAdmin);
        Assert.Equal(HttpStatusCode.NotFound, participantOnlyRoute.StatusCode); // Moderator_cannot_read_a_conversation_page
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.Conversation(w.ConversationId), w.RootAdmin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.ModerationReportAttachment(id, Guid.NewGuid()), w.RootAdmin)).StatusCode); // not an attachment of the reported message
    }

    [Fact]
    public async Task Remove_tombstones_the_message_for_participants_keeps_the_original_for_moderators_and_tells_only_the_sender()
    {
        var w = await SetUpAsync("MD8");
        await ReportAsync(w, 6);
        var id = await ReportIdAsync(w, 6);

        var resolved = await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(id), w.RootAdmin, new ResolveReportRequest(ModerationActions.Remove, null, null));

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var page = await ReadAsync<MessagePageDto>(await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(w.ConversationId), w.Reporter));
        var removed = page.Items.Single(m => m.Id == w.MessageIds[6]);
        Assert.Equal((MessageStates.Removed, null), (removed.State, removed.Body)); // the participants see a tombstone
        Assert.Equal(MyReportStatuses.Reviewed, removed.MyReportStatus); // the reporter learns "Reviewed" only
        var senderPage = await ReadAsync<MessagePageDto>(await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(w.ConversationId), w.Sender));
        Assert.All(senderPage.Items, m => Assert.Null(m.MyReportStatus)); // never for the sender or any other participant
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal("message 6", (await db.Set<Message>().SingleAsync(m => m.Id == w.MessageIds[6])).Body); // the original stays for moderators until retention
        var detail = await ReadAsync<MessageReportDto>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationReport(id), w.RootAdmin));
        Assert.Equal((ReportStatuses.ActionTaken, ModerationActions.Remove), (detail.Status, detail.Action));
        var senderNotices = await db.Set<Notification>().Where(n => n.UserId == w.Sender.UserId).Select(n => n.EventKey).ToListAsync();
        var reporterNotices = await db.Set<Notification>().Where(n => n.UserId == w.Reporter.UserId && n.EventKey == NotificationEvents.MessagingReportResolved).ToListAsync();
        Assert.Contains(NotificationEvents.MessagingWarning, senderNotices); // the sender is told of the removal ...
        Assert.DoesNotContain(NotificationEvents.MessagingReportResolved, senderNotices); // ... and never gets the reporter's notice
        var notice = Assert.Single(reporterNotices);
        Assert.Contains("Reviewed", notice.ParametersJson);
        Assert.DoesNotContain("Remove", notice.ParametersJson); // no action in the reporter's notice
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(id), w.RootAdmin, new ResolveReportRequest(ModerationActions.Dismiss, null, null))).StatusCode);
    }

    [Fact]
    public async Task Restrict_blocks_sending_until_the_end_and_lifting_restores_it_while_a_lock_stops_everyone()
    {
        var w = await SetUpAsync("MD9");
        await ReportAsync(w, 6);
        await ReportAsync(w, 8);
        var first = await ReportIdAsync(w, 6);
        var second = await ReportIdAsync(w, 8);
        var wrongDuration = await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(first), w.RootAdmin, new ResolveReportRequest(ModerationActions.Restrict, 14, null));
        var missingDuration = await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(first), w.RootAdmin, new ResolveReportRequest(ModerationActions.Restrict, null, null));
        var strayDuration = await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(first), w.RootAdmin, new ResolveReportRequest(ModerationActions.Warn, 7, null));
        Assert.All([wrongDuration, missingDuration, strayDuration], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));

        var restricted = await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(first), w.RootAdmin, new ResolveReportRequest(ModerationActions.Restrict, 7, null));
        Assert.Equal(HttpStatusCode.OK, restricted.StatusCode);
        await using (var db = factory.CreateDbContext(TestCurrentContext.For(w.Tree.Org.Id, w.Tree.Root.Path)))
        {
            var restriction = await db.Set<MessagingRestriction>().SingleAsync(r => r.UserId == w.Sender.UserId);
            Assert.InRange((restriction.UntilUtc!.Value - DateTime.UtcNow).TotalDays, 6.9, 7.1);
        }

        var listed = await ReadAsync<List<RestrictionDto>>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationRestrictions, w.RootAdmin));
        Assert.Equal(w.Sender.UserId, Assert.Single(listed).UserId);
        Assert.Empty(await ReadAsync<List<RestrictionDto>>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationRestrictions, w.School2Admin))); // outside the scope

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.ModerationRestriction(w.Sender.UserId), w.School2Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.ModerationRestriction(w.Sender.UserId), w.RootAdmin)).StatusCode);
        Assert.Empty(await ReadAsync<List<RestrictionDto>>(await SendAsync(factory, HttpMethod.Get, Routes.ModerationRestrictions, w.RootAdmin))); // lifted

        var locked = await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(second), w.RootAdmin, new ResolveReportRequest(ModerationActions.LockConversation, null, null));
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        var send = await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(w.ConversationId), w.Reporter, new SendMessageRequest("hello", null));
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode); // conversation_closed
    }

    [Fact]
    public async Task A_restriction_is_checked_by_the_policy_on_every_post_and_a_moderator_never_touches_a_block()
    {
        var w = await SetUpAsync("MD10");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new UserBlock(w.Tree.Org.Id, w.Reporter.UserId, w.Sender.UserId)); // the reporter blocked the sender before
            await db.SaveChangesAsync();
        }

        await ReportAsync(w, 6);
        var id = await ReportIdAsync(w, 6);
        var done = await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportResolve(id), w.RootAdmin, new ResolveReportRequest(ModerationActions.RemoveAndRestrict, 1, null));

        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        await using var read = factory.CreateDbContext(TestCurrentContext.For(w.Tree.Org.Id, w.Tree.Root.Path));
        Assert.Equal(1, await read.Set<UserBlock>().CountAsync(b => b.BlockerUserId == w.Reporter.UserId && b.BlockedUserId == w.Sender.UserId)); // the block is untouched
        Assert.True(await new Lms.Infrastructure.Messaging.Policy.MessagingPolicy(
            read, new Lms.Infrastructure.Messaging.Policy.RelationshipLookup(read), new Lms.Infrastructure.SystemClock(), TestCurrentContext.For(w.Tree.Org.Id, w.Tree.Root.Path)).IsRestrictedAsync(w.Sender.UserId));
        var moderation = typeof(Routes).GetFields().Where(f => f.IsLiteral && f.GetRawConstantValue() is string s && s.StartsWith("/api/moderation", StringComparison.Ordinal));
        Assert.DoesNotContain(moderation, f => ((string)f.GetRawConstantValue()!).Contains("block", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Safety_concern_sorts_first_and_notifies_the_org_admin_urgently_and_bulk_dismiss_skips_what_is_out_of_scope()
    {
        var w = await SetUpAsync("MD11");
        await ReportAsync(w, 2, ReportReasons.Spam);
        await ReportAsync(w, 4, ReportReasons.SafetyConcern);
        await ReportAsync(w, 6, ReportReasons.Harassment);

        var queue = await QueueAsync(w.RootAdmin);

        Assert.Equal([ReportReasons.SafetyConcern, ReportReasons.Spam, ReportReasons.Harassment], queue.Select(q => q.Reason)); // a safety concern first, then the oldest
        Assert.True(queue[0].IsSafety);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var notices = await db.Set<Notification>().Where(n => n.UserId == w.RootAdmin.UserId).Select(n => n.EventKey).ToListAsync();
            Assert.Contains(NotificationEvents.ModerationSafetyReportReceived, notices);
            Assert.Contains(NotificationEvents.ModerationReportReceived, notices);
        }

        var ids = queue.Select(q => q.Id).ToList();
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportsBulkDismiss, w.School2Admin, new BulkDismissRequest(ids, null))).StatusCode);
        Assert.Equal(3, (await QueueAsync(w.RootAdmin)).Count); // nothing outside their scope was dismissed
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportsBulkDismiss, w.School1Admin, new BulkDismissRequest(ids, null))).StatusCode);
        Assert.Empty(await QueueAsync(w.RootAdmin));
        Assert.Equal(3, (await QueueAsync(w.RootAdmin, "?status=Dismissed")).Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Post, Routes.ModerationReportsBulkDismiss, w.RootAdmin, new BulkDismissRequest([], null))).StatusCode);
    }

    [Fact]
    public async Task Message_reports_and_their_units_are_isolated_per_client()
    {
        var a = await SetUpAsync("MDIA");
        var b = await SetUpAsync("MDIB");
        await ReportAsync(a, 6);
        await ReportAsync(b, 6);
        var reportA = await ReportIdAsync(a, 6);
        var reportB = await ReportIdAsync(b, 6);

        await using var read = factory.CreateDbContext(TestCurrentContext.For(a.Tree.Org.Id, a.Tree.Root.Path));
        Assert.Equal([reportA], await read.Set<MessageReport>().Where(r => r.Id == reportA || r.Id == reportB).Select(r => r.Id).ToListAsync());
        var units = await read.Set<MessageReportUnit>().Where(u => u.ReportId == reportA || u.ReportId == reportB).Select(u => u.ReportId).Distinct().ToListAsync();
        Assert.Equal([reportA], units);
        await using var school2 = factory.CreateDbContext(TestCurrentContext.For(a.Tree.Org.Id, a.Tree.School2.Path));
        Assert.Empty(await school2.Set<MessageReportUnit>().Where(u => u.ReportId == reportA).ToListAsync()); // a sibling unit sees no unit row of it
    }
}
