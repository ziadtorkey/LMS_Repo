using System.Net;
using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Announcements;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Announcements;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Jobs;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Forums;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Announcements;

/// <summary>M6 Task 13 slice 2: the audience per target kind, publish, schedule, withdraw, expiry, the author cap and isolation.</summary>
[Collection(ApiCollection.Name)]
public sealed class AnnouncementPublishingTests(LmsApiFactory factory)
{
    private static AnnouncementTargetDto Cc(Guid id) => new(AnnouncementTargetKinds.ClassCourse, id, false);

    private static CreateAnnouncementRequest Request(
        Guid unit, IReadOnlyList<AnnouncementTargetDto> targets, IReadOnlyList<string>? chips = null, bool publish = true, DateTime? publishAt = null,
        DateTime? expires = null) =>
        new(unit, "عنوان", "Title", "<p>نص</p>", "<p>Text</p>", targets, chips, publishAt, expires, false, false, null, publish);

    private static async Task<AnnouncementDto> CreateOkAsync(ForumTestWorld w, RoleAssignment who, CreateAnnouncementRequest request)
    {
        var response = await w.SendAsync(who, HttpMethod.Post, Routes.OrgAnnouncements, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await w.ReadAsync<AnnouncementDto>(response);
    }

    private static async Task<int> PreviewAsync(
        ForumTestWorld w, RoleAssignment who, Guid unit, IReadOnlyList<AnnouncementTargetDto> targets, params string[] chips)
    {
        var response = await w.SendAsync(who, HttpMethod.Post, Routes.OrgAnnouncementAudiencePreview, new AudiencePreviewRequest(unit, targets, chips));
        return (await w.ReadAsync<AudiencePreviewDto>(response)).Count;
    }

    private async Task<List<AnnouncementRecipient>> RecipientsAsync(Guid announcementId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AnnouncementRecipient>().AsNoTracking().Where(r => r.AnnouncementId == announcementId).ToListAsync();
    }

    private async Task<Guid> AddGuardianOfAsync(ForumTestWorld w, params Guid[] studentUserIds)
    {
        var user = await PeopleTestKit.AddUserAsync(factory);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var guardian = new GuardianProfile(w.World.Tree.Org.Id, "ولي أمر", "Guardian", PeopleTestKit.NewMobile(), null, null);
        guardian.LinkUser(user.Id);
        db.Add(guardian);
        foreach (var studentUser in studentUserIds)
        {
            var student = await db.Set<StudentProfile>().SingleAsync(s => s.UserId == studentUser);
            db.Add(StudentGuardian.For(student, guardian, "Father", false));
        }

        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SetAsync<T>(ForumTestWorld w, SettingKey<T> key, T value)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<ISettingsResolver>().SetAsync(key, w.World.Tree.Root.Id, value, false);
    }

    /// <summary>Runs the keyed job the way the scheduler does: with the context of the author who scheduled it.</summary>
    private async Task RunJobAsAsync(RoleAssignment who, Func<AnnouncementJobs, Task> run)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<HttpCurrentContext>();
        context.SetUser(who.UserId, false);
        context.SetAssignment(who.Id, who.OrganizationId, who.OrgUnitPath);
        await run(scope.ServiceProvider.GetRequiredService<AnnouncementJobs>());
    }

    private async Task<Announcement> RowAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Announcement>().AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private Guid Root(ForumTestWorld w) => w.World.Tree.Root.Id;

    private Guid School(ForumTestWorld w) => w.World.Tree.School1.Id;

    // --- audience

    [Fact]
    public async Task Audience_equals_the_spec_for_each_target_kind()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP1");
        var levelType = await LevelOfClassAsync(w);
        var org = new AnnouncementTargetDto(AnnouncementTargetKinds.Organization, null, false);
        var unit = new AnnouncementTargetDto(AnnouncementTargetKinds.Unit, School(w), true);
        var cls = new AnnouncementTargetDto(AnnouncementTargetKinds.Class, w.World.Class.Id, false);
        var level = new AnnouncementTargetDto(AnnouncementTargetKinds.Level, levelType, false);

        Assert.Equal(2, await PreviewAsync(w, w.Admin, Root(w), [Cc(w.ClassCourseA)], AnnouncementRoleFilters.Students)); // students A and A2
        Assert.Equal(1, await PreviewAsync(w, w.Admin, Root(w), [Cc(w.ClassCourseA)], AnnouncementRoleFilters.Staff)); // teacher A
        Assert.Equal(3, await PreviewAsync(w, w.Admin, Root(w), [cls], AnnouncementRoleFilters.Students));
        Assert.Equal(3, await PreviewAsync(w, w.Admin, Root(w), [level], AnnouncementRoleFilters.Students));
        Assert.Equal(3, await PreviewAsync(w, w.Admin, Root(w), [unit], AnnouncementRoleFilters.Students));
        Assert.Equal(2, await PreviewAsync(w, w.Admin, Root(w), [unit], AnnouncementRoleFilters.Staff)); // teachers A and B (the admin is the actor)
        Assert.Equal(3, await PreviewAsync(w, w.Admin, Root(w), [org], AnnouncementRoleFilters.Students));
        Assert.Equal(5, await PreviewAsync(w, w.Admin, Root(w), [org], AnnouncementRoleFilters.Students, AnnouncementRoleFilters.Staff));
    }

    private async Task<Guid> LevelOfClassAsync(ForumTestWorld w)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var unitId = await db.Set<Lms.Domain.Classes.Class>().Where(c => c.Id == w.World.Class.Id).Select(c => c.OrgUnitId).SingleAsync();
        return await db.Set<Lms.Domain.Organizations.OrgUnit>().Where(u => u.Id == unitId).Select(u => u.LevelTypeId).SingleAsync();
    }

    [Fact]
    public async Task Guardians_chip_defaults_on_for_unit_authors_and_off_for_teachers()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP2");
        var guardian = await AddGuardianOfAsync(w, w.StudentA.UserId);

        var byAdmin = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)]));
        var byTeacher = await CreateOkAsync(w, w.TeacherA, Request(School(w), [Cc(w.ClassCourseA)]));

        Assert.Contains(await RecipientsAsync(byAdmin.Id), r => r.UserId == guardian && r.Audience == AnnouncementRoleFilters.Guardians);
        Assert.DoesNotContain(await RecipientsAsync(byTeacher.Id), r => r.UserId == guardian);
    }

    [Fact]
    public async Task A_guardian_of_two_targeted_students_gets_one_copy()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP3");
        var guardian = await AddGuardianOfAsync(w, w.StudentA.UserId, w.StudentA2.UserId);

        var made = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], [AnnouncementRoleFilters.Guardians]));

        Assert.Equal([guardian], (await RecipientsAsync(made.Id)).Select(r => r.UserId));
        Assert.Equal(1, (await RowAsync(made.Id)).AudienceCount);
    }

    [Fact]
    public async Task Expired_assignments_and_inactive_users_are_excluded_and_the_actor_never_is()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP4");
        var expired = await PeopleTestKit.AddUserAsync(factory);
        await AccessTestData.AssignAsync(factory, w.World.Tree, w.World.Tree.School1, BuiltInRoles.Teacher, expired.Id, validTo: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        var inactive = await PeopleTestKit.AddUserAsync(factory);
        await AccessTestData.AssignAsync(factory, w.World.Tree, w.World.Tree.School1, BuiltInRoles.Teacher, inactive.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Users.SingleAsync(u => u.Id == inactive.Id)).Status = UserStatus.Deactivated;
            await db.SaveChangesAsync();
        }

        var made = await CreateOkAsync(w, w.Admin, Request(Root(w), [new AnnouncementTargetDto(AnnouncementTargetKinds.Organization, null, false)], [AnnouncementRoleFilters.Staff]));
        var users = (await RecipientsAsync(made.Id)).Select(r => r.UserId).ToList();

        Assert.DoesNotContain(expired.Id, users);
        Assert.DoesNotContain(inactive.Id, users);
        Assert.DoesNotContain(w.Admin.UserId, users);
        Assert.Contains(w.TeacherA.UserId, users);
    }

    [Fact]
    public async Task Audience_never_includes_another_clients_user_and_stays_inside_the_authors_scope()
    {
        using var a = await ForumTestWorld.CreateAsync(factory, "AP5A");
        using var b = await ForumTestWorld.CreateAsync(factory, "AP5B");
        var made = await CreateOkAsync(a, a.Admin, Request(Root(a), [new AnnouncementTargetDto(AnnouncementTargetKinds.Organization, null, false)], [AnnouncementRoleFilters.Students, AnnouncementRoleFilters.Staff]));
        var users = (await RecipientsAsync(made.Id)).Select(r => r.UserId).ToHashSet();

        Assert.DoesNotContain(b.StudentA.UserId, users);
        Assert.DoesNotContain(b.TeacherA.UserId, users);

        // A unit admin of the other school reaches nobody of this school and cannot even target it.
        var otherAdmin = await AccessTestData.AssignAsync(factory, a.World.Tree, a.World.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var tryUnit = await a.SendAsync(otherAdmin, HttpMethod.Post, Routes.OrgAnnouncementAudiencePreview,
            new AudiencePreviewRequest(a.World.Tree.School2.Id, [new AnnouncementTargetDto(AnnouncementTargetKinds.Unit, School(a), true)], null));
        Assert.Equal(HttpStatusCode.NotFound, tryUnit.StatusCode);
        Assert.Equal(0, await PreviewAsync(a, otherAdmin, a.World.Tree.School2.Id, [new AnnouncementTargetDto(AnnouncementTargetKinds.Unit, a.World.Tree.School2.Id, true)], AnnouncementRoleFilters.Students));
    }

    [Fact]
    public async Task Preview_counts_per_chip()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP6");
        await AddGuardianOfAsync(w, w.StudentA.UserId);

        Assert.Equal(2, await PreviewAsync(w, w.Admin, Root(w), [Cc(w.ClassCourseA)], AnnouncementRoleFilters.Students));
        Assert.Equal(1, await PreviewAsync(w, w.Admin, Root(w), [Cc(w.ClassCourseA)], AnnouncementRoleFilters.Guardians));
        Assert.Equal(4, await PreviewAsync(w, w.Admin, Root(w), [Cc(w.ClassCourseA)])); // the unit-author default: all three chips
    }

    // --- publish, schedule, withdraw, expire

    [Fact]
    public async Task Publish_freezes_the_audience_and_a_second_publish_is_a_conflict()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP7");
        var draft = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], [AnnouncementRoleFilters.Students], publish: false));
        Assert.Equal(AnnouncementStatuses.Draft, draft.Status);

        var published = await w.ReadAsync<AnnouncementDto>(await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncementPublish(draft.Id)));
        var again = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncementPublish(draft.Id));
        var delete = await w.SendAsync(w.Admin, HttpMethod.Delete, Routes.OrgAnnouncement(draft.Id));

        Assert.Equal((AnnouncementStatuses.Published, 2), (published.Status, published.AudienceCount));
        Assert.Equal(2, (await RecipientsAsync(draft.Id)).Count);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task A_published_edit_sets_the_marker_without_a_new_audience()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP8");
        var made = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], [AnnouncementRoleFilters.Students]));

        var edit = await w.ReadAsync<AnnouncementDto>(await w.SendAsync(w.Admin, HttpMethod.Put, Routes.OrgAnnouncement(made.Id),
            new UpdateAnnouncementRequest("لا يتغير", "Ignored", null, "<p>Corrected</p>", [Cc(w.ClassCourseB)], null, null, null, true, false, null)));

        Assert.NotNull(edit.EditedAtUtc);
        Assert.Contains("Corrected", edit.BodyEn);
        Assert.Equal("Title", edit.TitleEn); // a published title never changes
        Assert.Equal([w.ClassCourseA], edit.Targets.Select(t => t.TargetId!.Value));
        Assert.Equal(2, (await RecipientsAsync(made.Id)).Count);
    }

    [Fact]
    public async Task A_scheduled_announcement_publishes_under_the_authors_context_when_its_job_runs()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP9");
        var made = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], [AnnouncementRoleFilters.Students], publishAt: DateTime.UtcNow.AddHours(2)));
        Assert.Equal((AnnouncementStatuses.Scheduled, null), (made.Status, made.AudienceCount));
        Assert.Empty(await RecipientsAsync(made.Id));

        await RunJobAsAsync(w.Admin, j => j.PublishAsync(made.Id, default));

        var row = await RowAsync(made.Id);
        Assert.Equal((AnnouncementStates.Published, 2), (row.Status, row.AudienceCount));
        Assert.Equal(2, (await RecipientsAsync(made.Id)).Count);
    }

    [Fact]
    public async Task Publish_job_with_a_foreign_org_payload_does_nothing()
    {
        using var a = await ForumTestWorld.CreateAsync(factory, "AP10A");
        using var b = await ForumTestWorld.CreateAsync(factory, "AP10B");
        var made = await CreateOkAsync(a, a.Admin, Request(Root(a), [Cc(a.ClassCourseA)], publishAt: DateTime.UtcNow.AddHours(2)));

        await RunJobAsAsync(b.Admin, j => j.PublishAsync(made.Id, default));

        Assert.Equal(AnnouncementStates.Scheduled, (await RowAsync(made.Id)).Status);
        Assert.Empty(await RecipientsAsync(made.Id));
    }

    [Fact]
    public async Task Schedule_moves_on_edit_and_cancels_on_withdraw()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP11");
        var made = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], publishAt: DateTime.UtcNow.AddHours(2)));
        var moved = await w.SendAsync(w.Admin, HttpMethod.Put, Routes.OrgAnnouncement(made.Id),
            new UpdateAnnouncementRequest("ع", "T", null, "<p>x</p>", [Cc(w.ClassCourseA)], null, DateTime.UtcNow.AddHours(3), null, false, false, null));
        var past = await w.SendAsync(w.Admin, HttpMethod.Put, Routes.OrgAnnouncement(made.Id),
            new UpdateAnnouncementRequest("ع", "T", null, "<p>x</p>", [Cc(w.ClassCourseA)], null, DateTime.UtcNow.AddHours(-1), null, false, false, null));
        using var scope = factory.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobs>();

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.True(jobs.Cancel(AnnouncementService.PublishKey(made.Id))); // exactly one pending job under the key
        Assert.False(jobs.Cancel(AnnouncementService.PublishKey(made.Id)));

        var again = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], publishAt: DateTime.UtcNow.AddHours(2)));
        var withdrawn = await w.ReadAsync<AnnouncementDto>(await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncementWithdraw(again.Id)));

        Assert.Equal(AnnouncementStatuses.Withdrawn, withdrawn.Status);
        Assert.False(jobs.Cancel(AnnouncementService.PublishKey(again.Id))); // withdraw already cancelled it
    }

    [Fact]
    public async Task Withdraw_needs_a_published_or_scheduled_announcement_in_the_callers_reach()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP12");
        var draft = await CreateOkAsync(w, w.TeacherA, Request(School(w), [Cc(w.ClassCourseA)], publish: false));
        var published = await CreateOkAsync(w, w.TeacherA, Request(School(w), [Cc(w.ClassCourseA)]));

        Assert.Equal(HttpStatusCode.Conflict, (await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.OrgAnnouncementWithdraw(draft.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.TeacherB, HttpMethod.Post, Routes.OrgAnnouncementWithdraw(published.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.OrgAnnouncementWithdraw(published.Id))).StatusCode);
    }

    [Fact]
    public async Task The_expiry_job_settles_the_status()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP13");
        var made = await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], expires: DateTime.UtcNow.AddMinutes(1)));

        await RunJobAsAsync(w.Admin, j => j.ExpireAsync(made.Id, DateTime.UtcNow.AddMinutes(2), default));

        Assert.Equal(AnnouncementStates.Expired, (await RowAsync(made.Id)).Status);
    }

    [Fact]
    public async Task Author_cap_returns_429_and_drafts_do_not_count()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AP14");
        await SetAsync(w, SettingKeys.AnnouncementsMaxPerAuthorPerDay, 1);

        await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)], publish: false));
        await CreateOkAsync(w, w.Admin, Request(Root(w), [Cc(w.ClassCourseA)]));
        var second = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncements, Request(Root(w), [Cc(w.ClassCourseA)]));

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(ProblemCodes.rate_limited, await ForumTestWorld.CodeAsync(second));
    }

    [Fact]
    public async Task Recipient_rows_of_client_B_are_invisible_in_client_A_context()
    {
        using var a = await ForumTestWorld.CreateAsync(factory, "AP15A");
        using var b = await ForumTestWorld.CreateAsync(factory, "AP15B");
        var madeB = await CreateOkAsync(b, b.Admin, Request(Root(b), [Cc(b.ClassCourseA)], [AnnouncementRoleFilters.Students]));
        await CreateOkAsync(a, a.Admin, Request(Root(a), [Cc(a.ClassCourseA)], [AnnouncementRoleFilters.Students]));

        await using var asA = factory.CreateDbContext(TestCurrentContext.For(a.World.Tree.Org.Id, a.World.Tree.Root.Path));
        await using var noClient = factory.CreateDbContext(new TestCurrentContext { UserId = a.StudentA.UserId });

        Assert.DoesNotContain(await asA.Set<AnnouncementRecipient>().ToListAsync(), r => r.AnnouncementId == madeB.Id);
        Assert.Equal(2, await asA.Set<AnnouncementRecipient>().CountAsync());
        Assert.Empty(await noClient.Set<AnnouncementRecipient>().ToListAsync());
    }
}
