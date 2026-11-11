using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure;
using Lms.Infrastructure.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class StatusGateTests(LmsApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<HttpResponseMessage> GetMeAsync(Guid user, Guid? assignment = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Routes.Me);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        if (assignment is not null)
        {
            request.Headers.Add(ContextHeader.Name, assignment.ToString());
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private async Task<IReadOnlyList<UserContextDto>> ContextsAsync(Guid user) =>
        (await (await GetMeAsync(user)).Content.ReadFromJsonAsync<MeDto>())!.Contexts;

    private async Task ChangeAsync(PersonKind kind, Guid profileId, PersonStatus to)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        await new PersonStatusService(db, TestCurrentContext.PlatformAdmin(), new SystemClock())
            .ChangeAsync(kind, profileId, to, "test", default);
    }

    private async Task<User> StatusOfAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    [Fact]
    public async Task Deactivated_staff_loses_staff_contexts_on_next_request()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SG1");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, userId: user);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(user, assignment.Id)).StatusCode);

        await ChangeAsync(PersonKind.Staff, staff.Id, PersonStatus.Deactivated);

        // The same context header that worked a moment ago is refused, and the chooser is empty.
        Assert.Equal(HttpStatusCode.Forbidden, (await GetMeAsync(user, assignment.Id)).StatusCode);
        Assert.Empty(await ContextsAsync(user));
    }

    [Fact]
    public async Task Reactivation_restores_access()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SG2");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, userId: user);
        await ChangeAsync(PersonKind.Staff, staff.Id, PersonStatus.Deactivated);
        Assert.Empty(await ContextsAsync(user));

        await ChangeAsync(PersonKind.Staff, staff.Id, PersonStatus.Active);

        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(user, assignment.Id)).StatusCode);
        Assert.Single(await ContextsAsync(user));
        Assert.Equal(UserStatus.Active, (await StatusOfAsync(user)).Status);
    }

    [Fact]
    public async Task Staff_past_ExpiresOn_is_refused_before_any_job_runs()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SG3");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        // Status is still Active (the daily job has not run), but the last usable day was yesterday.
        await PeopleTestKit.AddStaffAsync(factory, tree.School1, userId: user, expiresOn: Today.AddDays(-1));

        Assert.Equal(HttpStatusCode.Forbidden, (await GetMeAsync(user, assignment.Id)).StatusCode);
        Assert.Empty(await ContextsAsync(user));
    }

    [Fact]
    public async Task Staff_on_the_ExpiresOn_day_still_has_access()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SG4");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        await PeopleTestKit.AddStaffAsync(factory, tree.School1, userId: user, expiresOn: Today);

        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(user, assignment.Id)).StatusCode);
    }

    [Fact]
    public async Task Teacher_who_is_guardian_keeps_guardian_context_after_staff_deactivation()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SG5");
        var user = Guid.NewGuid();
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        var guardian = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, user);
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, userId: user);
        await PeopleTestKit.AddGuardianAsync(factory, tree.Org.Id, PeopleTestKit.NewMobile(), userId: user);

        await ChangeAsync(PersonKind.Staff, staff.Id, PersonStatus.Deactivated);

        var contexts = await ContextsAsync(user);
        Assert.Equal(guardian.Id, Assert.Single(contexts).AssignmentId);
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(user, guardian.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetMeAsync(user, teacher.Id)).StatusCode);
        Assert.Equal(UserStatus.Active, (await StatusOfAsync(user)).Status); // the guardian profile keeps the account active
    }

    [Fact]
    public async Task Status_gate_is_per_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "SG6A");
        var b = await AccessTestData.SeedAsync(factory, "SG6B");
        var user = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);
        var staffA = await PeopleTestKit.AddStaffAsync(factory, a.School1, userId: user);
        await PeopleTestKit.AddStaffAsync(factory, b.School1, userId: user);

        await ChangeAsync(PersonKind.Staff, staffA.Id, PersonStatus.Deactivated);

        Assert.Equal(inB.Id, Assert.Single(await ContextsAsync(user)).AssignmentId);
        Assert.Equal(UserStatus.Active, (await StatusOfAsync(user)).Status); // still active in client B
    }

    [Fact]
    public async Task Bare_user_without_profile_keeps_access()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SG7");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);

        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(user, assignment.Id)).StatusCode);
    }

    [Fact]
    public async Task User_status_becomes_Deactivated_only_when_no_profile_is_active_in_any_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "SG8A");
        var b = await AccessTestData.SeedAsync(factory, "SG8B");
        var user = Guid.NewGuid();
        var staffA = await PeopleTestKit.AddStaffAsync(factory, a.School1, userId: user);
        var staffB = await PeopleTestKit.AddStaffAsync(factory, b.School1, userId: user);

        await ChangeAsync(PersonKind.Staff, staffA.Id, PersonStatus.Deactivated);
        Assert.Equal(UserStatus.Active, (await StatusOfAsync(user)).Status);

        await ChangeAsync(PersonKind.Staff, staffB.Id, PersonStatus.Deactivated);
        Assert.Equal(UserStatus.Deactivated, (await StatusOfAsync(user)).Status); // login shows AccountDeactivated

        await ChangeAsync(PersonKind.Staff, staffB.Id, PersonStatus.Active);
        Assert.Equal(UserStatus.Active, (await StatusOfAsync(user)).Status);
    }

    [Fact]
    public async Task All_profiles_expired_gives_AccountExpired_message()
    {
        var a = await AccessTestData.SeedAsync(factory, "SG9A");
        var b = await AccessTestData.SeedAsync(factory, "SG9B");
        var user = Guid.NewGuid();
        var staffA = await PeopleTestKit.AddStaffAsync(factory, a.School1, userId: user);
        var staffB = await PeopleTestKit.AddStaffAsync(factory, b.School1, userId: user);

        await ChangeAsync(PersonKind.Staff, staffA.Id, PersonStatus.Expired);
        await ChangeAsync(PersonKind.Staff, staffB.Id, PersonStatus.Expired);
        Assert.Equal(UserStatus.Expired, (await StatusOfAsync(user)).Status); // login shows AccountExpired

        await ChangeAsync(PersonKind.Staff, staffB.Id, PersonStatus.Deactivated);
        Assert.Equal(UserStatus.Deactivated, (await StatusOfAsync(user)).Status); // mixed: deactivated wins
    }

    [Fact]
    public async Task Change_writes_a_history_row_and_platform_admins_keep_their_status()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SG10");
        var user = Guid.NewGuid();
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, userId: user);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Users.SingleAsync(u => u.Id == user)).IsPlatformAdmin = true;
            await db.SaveChangesAsync();
        }

        await ChangeAsync(PersonKind.Staff, staff.Id, PersonStatus.Deactivated);

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var change = await read.Set<PersonStatusChange>().SingleAsync(c => c.ProfileId == staff.Id);
        Assert.Equal((PersonKind.Staff, PersonStatus.Active, PersonStatus.Deactivated, "test"),
            (change.Kind, change.From, change.To, change.Reason));
        Assert.Equal(UserStatus.Active, (await StatusOfAsync(user)).Status);
    }
}
