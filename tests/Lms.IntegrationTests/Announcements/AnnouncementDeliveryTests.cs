using System.Net;
using Lms.Api.Infrastructure;
using Lms.Domain.Announcements;
using Lms.Domain.Comms;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.Comms;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Forums;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Forums;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Announcements;

/// <summary>M6 Task 14: delivery through the publisher and the outbox, the reader feed with read state, and the merged course page.</summary>
[Collection(ApiCollection.Name)]
public sealed class AnnouncementDeliveryTests(LmsApiFactory factory)
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    private static AnnouncementTargetDto Cc(Guid id) => new(AnnouncementTargetKinds.ClassCourse, id, false);

    private static CreateAnnouncementRequest Request(
        Guid unit, IReadOnlyList<AnnouncementTargetDto> targets, bool notify = true, bool urgent = false, bool pinned = false, DateTime? expires = null,
        IReadOnlyList<string>? chips = null) =>
        new(unit, "عنوان", "Title", "<p>نص سري</p>", "<p>Secret text</p>", targets, chips ?? [AnnouncementRoleFilters.Students], null, expires, pinned, notify, null, true, urgent);

    private async Task<AnnouncementDto> CreateOkAsync(ForumTestWorld w, Lms.Domain.Access.RoleAssignment who, CreateAnnouncementRequest request)
    {
        var response = await w.SendAsync(who, HttpMethod.Post, Routes.OrgAnnouncements, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await w.ReadAsync<AnnouncementDto>(response);
    }

    private async Task<List<Notification>> NoticesAsync(Guid user)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().AsNoTracking().Where(n => n.UserId == user && n.EventKey == NotificationEvents.AnnouncementsPublished).ToListAsync();
    }

    private async Task<List<DeliveryOutbox>> OutboxAsync(Guid user)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<DeliveryOutbox>().AsNoTracking().Where(r => r.UserId == user).ToListAsync();
    }

    private async Task SetAsync<T>(ForumTestWorld w, SettingKey<T> key, T value)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<ISettingsResolver>().SetAsync(key, w.World.Tree.Root.Id, value, false);
    }

    private async Task PreferEmailAsync(Guid user)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new NotificationPreference(user, NotificationEvents.AnnouncementsPublished, NotificationChannels.Email, true));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AddGuardianOfAsync(ForumTestWorld w, Guid studentUser)
    {
        var user = await PeopleTestKit.AddUserAsync(factory);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var guardian = new GuardianProfile(w.World.Tree.Org.Id, "ولي أمر", "Guardian", PeopleTestKit.NewMobile(), null, null);
        guardian.LinkUser(user.Id);
        db.Add(guardian);
        db.Add(StudentGuardian.For(await db.Set<StudentProfile>().SingleAsync(s => s.UserId == studentUser), guardian, "Father", false));
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<PagedResult<MyAnnouncementDto>> FeedAsync(ForumTestWorld w, Lms.Domain.Access.RoleAssignment who) =>
        await w.ReadAsync<PagedResult<MyAnnouncementDto>>(await w.SendAsync(who, HttpMethod.Get, Routes.MyAnnouncements));

    // --- delivery

    [Fact]
    public async Task Fan_out_goes_through_the_publisher_in_app_by_default_and_by_email_only_when_urgent()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD1");
        await PreferEmailAsync(w.StudentA.UserId); // a user can only narrow: this opt-in cannot switch on a channel the event does not offer

        var normal = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)]));

        Assert.Single(await NoticesAsync(w.StudentA.UserId));
        Assert.Single(await NoticesAsync(w.StudentA2.UserId));
        Assert.Empty(await NoticesAsync(w.StudentB.UserId)); // not a recipient
        Assert.Empty(await OutboxAsync(w.StudentA.UserId));
        Assert.All(await NoticesAsync(w.StudentA.UserId), n => Assert.Equal(normal.Id, n.SourceId));
        Assert.Equal([("InApp", "Sent", 2)], (await ReadAsync(w, normal.Id)).DeliverySummary!.Counts.Select(c => (c.Channel, c.Status, c.Count)));

        var urgent = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], urgent: true));

        Assert.Single(await OutboxAsync(w.StudentA.UserId));
        Assert.Single(await OutboxAsync(w.StudentA2.UserId));
        Assert.Contains(("Email", "Pending", 2), (await ReadAsync(w, urgent.Id)).DeliverySummary!.Counts.Select(c => (c.Channel, c.Status, c.Count)));
    }

    private async Task<AnnouncementDto> ReadAsync(ForumTestWorld w, Guid id) =>
        await w.ReadAsync<AnnouncementDto>(await w.SendAsync(w.Admin, HttpMethod.Get, Routes.OrgAnnouncement(id)));

    [Fact]
    public async Task Announcements_published_does_not_email_by_default_and_only_urgent_does()
    {
        // The catalog decision (#766): the event offers in-app only, so no preference can add email; urgency is the one way to email.
        Assert.Equal([NotificationChannels.InApp], NotificationEvents.Find(NotificationEvents.AnnouncementsPublished)!.DefaultChannels ?? [NotificationChannels.InApp]);

        using var w = await ForumTestWorld.CreateAsync(factory, "AD1B");
        await PreferEmailAsync(w.StudentA.UserId);

        await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)]));
        Assert.Empty(await OutboxAsync(w.StudentA.UserId));
        Assert.Empty(await OutboxAsync(w.StudentA2.UserId));

        await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], urgent: true));
        Assert.Equal(NotificationChannels.Email, Assert.Single(await OutboxAsync(w.StudentA.UserId)).Channel);
        Assert.Single(await OutboxAsync(w.StudentA2.UserId));
    }

    [Fact]
    public async Task A_silent_post_sends_no_notification()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD2");

        var made = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], notify: false));

        Assert.Empty(await NoticesAsync(w.StudentA.UserId));
        Assert.Equal(2, made.AudienceCount); // still readable in the feed
        Assert.Single((await FeedAsync(w, w.StudentA)).Items);
    }

    [Fact]
    public async Task The_notice_carries_the_title_and_never_the_body()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD3");

        await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)]));

        var notice = Assert.Single(await NoticesAsync(w.StudentA.UserId));
        Assert.Contains("Title", notice.ParametersJson);
        Assert.DoesNotContain("Secret", notice.ParametersJson);
        Assert.DoesNotContain("سري", notice.ParametersJson);
    }

    [Fact]
    public async Task Urgent_announcement_bypasses_quiet_hours_reaches_guardians_by_email_and_is_pinned()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD4");
        var guardian = await AddGuardianOfAsync(w, w.StudentA.UserId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Riyadh);
        await SetAsync(w, SettingKeys.CommsQuietHoursEnabled, true);
        await SetAsync(w, SettingKeys.CommsQuietHoursStart, local.AddHours(-1).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        await SetAsync(w, SettingKeys.CommsQuietHoursEnd, local.AddHours(1).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

        var urgent = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], urgent: true, chips: [AnnouncementRoleFilters.Students, AnnouncementRoleFilters.Guardians]));
        var normal = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], chips: [AnnouncementRoleFilters.Students, AnnouncementRoleFilters.Guardians]));

        Assert.True(urgent.IsPinned);
        var email = Assert.Single(await OutboxAsync(guardian)); // the urgent one by rule; the normal one is in-app only unless the guardian opts in
        Assert.Equal((NotificationChannels.Email, true), (email.Channel, email.IsPriority));
        Assert.Null(email.HeldUntilUtc);
        Assert.Equal(DeliveryStates.Pending, email.Status);
        Assert.NotNull(normal.AudienceCount);
    }

    [Fact]
    public async Task An_urgent_announcement_cannot_carry_a_schedule()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD5");
        var request = Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], urgent: true) with { PublishAtUtc = DateTime.UtcNow.AddHours(5) };

        var response = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncements, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task My_feed_is_pinned_first_and_hides_expired_and_withdrawn()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD7");
        var plain = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], notify: false));
        var pinned = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], notify: false, pinned: true));
        var withdrawn = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], notify: false));
        var expiring = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], notify: false, expires: DateTime.UtcNow.AddMinutes(1)));
        await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncementWithdraw(withdrawn.Id));

        var feed = await FeedAsync(w, w.StudentA);
        Assert.Equal([pinned.Id, expiring.Id, plain.Id], feed.Items.Select(i => i.Id));
        Assert.DoesNotContain(feed.Items, i => i.Id == withdrawn.Id);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Announcements SET ExpiresAtUtc = {DateTime.UtcNow.AddMinutes(-1)} WHERE Id = {expiring.Id}");
        }

        Assert.DoesNotContain((await FeedAsync(w, w.StudentA)).Items, i => i.Id == expiring.Id);
    }

    [Fact]
    public async Task Read_state_is_per_recipient_and_a_non_recipient_gets_404()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD8");
        var made = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], notify: false));

        var read = await w.SendAsync(w.StudentA, HttpMethod.Post, Routes.MyAnnouncementRead(made.Id));
        var outsider = await w.SendAsync(w.StudentB, HttpMethod.Post, Routes.MyAnnouncementRead(made.Id));

        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.True(Assert.Single((await FeedAsync(w, w.StudentA)).Items).IsRead);
        Assert.False(Assert.Single((await FeedAsync(w, w.StudentA2)).Items).IsRead);
        Assert.Empty((await FeedAsync(w, w.StudentB)).Items);
    }

    [Fact]
    public async Task Read_state_and_feed_stay_inside_the_client()
    {
        using var a = await ForumTestWorld.CreateAsync(factory, "AD9A");
        using var b = await ForumTestWorld.CreateAsync(factory, "AD9B");
        var made = await CreateOkAsync(b, b.Admin, Request(b.World.Tree.Root.Id, [Cc(b.ClassCourseA)], notify: false));

        Assert.Equal(HttpStatusCode.NotFound, (await a.SendAsync(a.StudentA, HttpMethod.Post, Routes.MyAnnouncementRead(made.Id))).StatusCode);
        Assert.Empty((await FeedAsync(a, a.StudentA)).Items);
    }

    // --- merged course page

    [Fact]
    public async Task The_course_page_lists_board_posts_and_class_course_announcements_once_with_their_source()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD10");
        var board = await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.ClassCourseAnnouncements(w.ClassCourseA), new CreateCourseAnnouncementRequest("Board post", "<p>b</p>", false, false));
        Assert.Equal(HttpStatusCode.Created, board.StatusCode);
        var school = await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA), new AnnouncementTargetDto(AnnouncementTargetKinds.Class, w.World.Class.Id, false)], notify: false));
        await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseB)], notify: false)); // another course: not listed here

        var list = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));

        Assert.Equal(2, list.Total);
        Assert.Single(list.Items, i => i.Source == AnnouncementSources.CourseBoard && i.Title == "Board post");
        Assert.Single(list.Items, i => i.Source == AnnouncementSources.Announcement && i.Id == school.Id); // two targets reach the course, listed once
    }

    [Fact]
    public async Task The_course_page_does_not_show_a_school_announcement_to_someone_it_did_not_reach()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AD11");
        await CreateOkAsync(w, w.Admin, Request(w.World.Tree.Root.Id, [Cc(w.ClassCourseA)], notify: false, chips: [AnnouncementRoleFilters.Staff]));

        var asStudent = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));
        var asTeacher = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.TeacherA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));

        Assert.Empty(asStudent.Items);
        Assert.Single(asTeacher.Items);
    }
}
