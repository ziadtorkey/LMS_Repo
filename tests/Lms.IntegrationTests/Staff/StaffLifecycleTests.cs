using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Auth;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Lms.Shared.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Staff;

/// <summary>M2 Task 11 (#163): edit, deactivate/reactivate with reason, delete and resend invitation.</summary>
[Collection(ApiCollection.Name)]
public sealed class StaffLifecycleTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;
    private readonly FakeEmailSender _emails = new();

    public StaffLifecycleTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_emails)));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RoleAssignment caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _host.CreateClient().SendAsync(request);
    }

    private async Task<(AccessTree Tree, RoleAssignment Admin, StaffDetailDto Staff)> SetUpAsync(string name, string? role = null)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var created = await SendAsync(HttpMethod.Post, Routes.Staff, admin, new CreateStaffRequest(
            "معلم", "Teacher", $"lc{Guid.NewGuid():N}"[..14] + "@lms.test", null, null, null, null, null, tree.School1.Id, null,
            [new StaffRoleInput(tree.Roles[role ?? BuiltInRoles.Teacher].Id, tree.School1.Id, null, null)]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (tree, admin, (await created.Content.ReadFromJsonAsync<StaffDetailDto>(Web))!);
    }

    private static UpdateStaffRequest Update(StaffDetailDto staff, Guid? home = null, string? nameEn = null, DateOnly? expires = null, string? mobile = null, string? nationalId = null) =>
        new("اسم جديد", nameEn ?? "New Name", mobile, nationalId, "مدرس", "Teacher", null, home ?? staff.Summary.HomeOrgUnitId, expires);

    private Task<HttpResponseMessage> StatusAsync(RoleAssignment caller, Guid id, string status, string reason = "because") =>
        SendAsync(HttpMethod.Put, Routes.StaffStatus(id), caller, new SetPersonStatusRequest(status, reason));

    private async Task<StaffDetailDto> GetAsync(RoleAssignment caller, Guid id) =>
        (await (await SendAsync(HttpMethod.Get, Routes.StaffMember(id), caller)).Content.ReadFromJsonAsync<StaffDetailDto>(Web))!;

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<IReadOnlyList<UserContextDto>> ContextsAsync(Guid user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Routes.Me);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        return (await (await _factory.CreateClient().SendAsync(request)).Content.ReadFromJsonAsync<MeDto>(Web))!.Contexts;
    }

    [Fact]
    public async Task Edit_updates_profile_and_audits_before_after()
    {
        var (_, admin, staff) = await SetUpAsync("LC1");

        var response = await SendAsync(HttpMethod.Put, Routes.StaffMember(staff.Summary.Id), admin,
            Update(staff, nameEn: "Renamed", mobile: "+20 100 123 4567", nationalId: "ab99999"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<StaffDetailDto>(Web))!;
        Assert.Equal("Renamed", dto.Summary.NameEn);
        Assert.Equal("+201001234567", dto.Summary.Mobile);
        Assert.Equal("AB99999", dto.NationalId);
        Assert.Equal(staff.Summary.Email, dto.Summary.Email); // the login identity never changes
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.AuditEntries.IgnoreQueryFilters().SingleAsync(e =>
            e.EntityType == nameof(StaffProfile) && e.EntityId == staff.Summary.Id && e.Action == AuditAction.Updated);
        Assert.Contains("Renamed", entry.AfterJson);
        Assert.Contains("Teacher", entry.BeforeJson);
    }

    [Fact]
    public async Task Edit_rejects_duplicates_but_not_the_person_itself()
    {
        var (tree, admin, first) = await SetUpAsync("LC2");
        var second = await SendAsync(HttpMethod.Post, Routes.Staff, admin, new CreateStaffRequest(
            "ثان", "Second", $"lc{Guid.NewGuid():N}"[..14] + "@lms.test", "+201001112222", null, null, null, null, tree.School1.Id, null,
            [new StaffRoleInput(tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id, null, null)]));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var duplicate = await SendAsync(HttpMethod.Put, Routes.StaffMember(first.Summary.Id), admin, Update(first, mobile: "+201001112222"));
        var unchanged = await SendAsync(HttpMethod.Put, Routes.StaffMember(first.Summary.Id), admin, Update(first, mobile: "+201003334444"));
        var same = await SendAsync(HttpMethod.Put, Routes.StaffMember(first.Summary.Id), admin, Update(first, mobile: "+201003334444"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(ProblemCodes.duplicate_person, await CodeAsync(duplicate));
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode); // saving the same values again is not a duplicate of itself
    }

    [Fact]
    public async Task Moving_home_needs_permission_at_the_new_home_and_history_follows_the_person()
    {
        var (tree, admin, staff) = await SetUpAsync("LC3");
        var school1Admin = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school2Admin = await AccessTestData.AssignAsync(_factory, tree, tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Deactivated, "left for leave")).StatusCode);

        // School1's admin may not move the person to School2 (404: outside their scope)
        var refused = await SendAsync(HttpMethod.Put, Routes.StaffMember(staff.Summary.Id), school1Admin, Update(staff, home: tree.School2.Id));
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);

        var moved = await SendAsync(HttpMethod.Put, Routes.StaffMember(staff.Summary.Id), admin, Update(staff, home: tree.School2.Id));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        // the new home's admin sees the history; the old unit's admin no longer sees the person at all
        var history = (await GetAsync(school2Admin, staff.Summary.Id)).StatusHistory;
        Assert.Equal("left for leave", Assert.Single(history).Reason);
        // School1 keeps seeing them only through their remaining Teacher assignment there: limited view, no history (D29)
        var oldView = await GetAsync(school1Admin, staff.Summary.Id);
        Assert.True(oldView.IsLimitedView);
        Assert.Empty(oldView.StatusHistory);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.Set<PersonStatusChange>().IgnoreQueryFilters().SingleAsync(c => c.ProfileId == staff.Summary.Id);
        Assert.Equal(tree.School2.Path, row.OrgUnitPath);
        Assert.Equal(tree.School2.Id, row.OrgUnitId);
    }

    [Fact]
    public async Task Deactivate_requires_reason_and_writes_history()
    {
        var (_, admin, staff) = await SetUpAsync("LC4");

        var noReason = await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Deactivated, "  ");
        var bogus = await StatusAsync(admin, staff.Summary.Id, "Expired", "x");
        var ok = await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Deactivated, "contract ended");

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode); // only Active and Deactivated can be requested
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var detail = await GetAsync(admin, staff.Summary.Id);
        Assert.Equal(PersonStatuses.Deactivated, detail.Summary.Status);
        var change = Assert.Single(detail.StatusHistory);
        Assert.Equal(PersonStatuses.Active, change.FromStatus);
        Assert.Equal(PersonStatuses.Deactivated, change.ToStatus);
        Assert.Equal("contract ended", change.Reason);
    }

    [Fact]
    public async Task Deactivated_staff_cannot_log_in_and_sees_deactivated_message()
    {
        var (_, admin, staff) = await SetUpAsync("LC5");
        var authHost = new AuthTestHost(_factory);
        using (var scope = authHost.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(staff.Summary.UserId.ToString()))!;
            Assert.True((await users.AddPasswordAsync(user, AuthTestHost.Password)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Deactivated)).StatusCode);

        using var browser = authHost.NewBrowser();
        var response = await AuthTestHost.PostLoginAsync(browser, staff.Summary.Email, AuthTestHost.Password,
            loginUrl: "/Account/Login?culture=en&ui-culture=en");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("This account is deactivated. Please contact your administrator.",
            AuthTestHost.TestId(await response.Content.ReadAsStringAsync(), "login-error"));
    }

    [Fact]
    public async Task Reactivate_restores_contexts()
    {
        var (_, admin, staff) = await SetUpAsync("LC6");
        Assert.Single(await ContextsAsync(staff.Summary.UserId));

        await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Deactivated);
        Assert.Empty(await ContextsAsync(staff.Summary.UserId));

        var back = await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Active, "returned");
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Single(await ContextsAsync(staff.Summary.UserId));
        Assert.Equal(2, (await GetAsync(admin, staff.Summary.Id)).StatusHistory.Count);
    }

    [Fact]
    public async Task Reactivating_an_expired_profile_needs_a_future_or_no_expiry()
    {
        var (_, admin, staff) = await SetUpAsync("LC7");
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var profile = await db.Set<StaffProfile>().SingleAsync(p => p.Id == staff.Summary.Id);
            profile.Update(profile.NameAr, profile.NameEn, null, null, null, null, null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3));
            profile.SetStatus(PersonStatus.Expired);
            await db.SaveChangesAsync();
        }

        var refused = await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Active, "back");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(ProblemCodes.invalid_expiry, await CodeAsync(refused));

        var extended = await SendAsync(HttpMethod.Put, Routes.StaffMember(staff.Summary.Id), admin,
            Update(staff, expires: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)));
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(admin, staff.Summary.Id, PersonStatuses.Active, "back")).StatusCode);
    }

    [Fact]
    public async Task Cannot_deactivate_or_delete_self()
    {
        var (tree, _, _) = await SetUpAsync("LC8");
        var selfAdmin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        Guid ownProfile;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var me = new User { UserName = $"me{Guid.NewGuid():N}@lms.test", DisplayNameEn = "Me" };
            me.Email = me.UserName;
            db.Users.Add(me);
            db.RoleAssignments.Remove(await db.RoleAssignments.SingleAsync(a => a.Id == selfAdmin.Id));
            selfAdmin = new RoleAssignment(await db.OrgUnits.SingleAsync(u => u.Id == tree.Root.Id), me.Id, tree.Roles[BuiltInRoles.OrgAdmin].Id);
            db.RoleAssignments.Add(selfAdmin);
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == tree.Root.Id);
            var profile = new StaffProfile(unit, selfAdmin.UserId, "أنا", "Me", $"me{Guid.NewGuid():N}"[..14] + "@lms.test", null, null, null, null, null, null);
            db.Set<StaffProfile>().Add(profile);
            await db.SaveChangesAsync();
            ownProfile = profile.Id;
        }

        var status = await StatusAsync(selfAdmin, ownProfile, PersonStatuses.Deactivated);
        var delete = await SendAsync(HttpMethod.Delete, Routes.StaffMember(ownProfile), selfAdmin);

        Assert.Equal(HttpStatusCode.Conflict, status.StatusCode);
        Assert.Equal(ProblemCodes.cannot_change_own_status, await CodeAsync(status));
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(ProblemCodes.cannot_change_own_status, await CodeAsync(delete));
    }

    [Fact]
    public async Task Delete_soft_deletes_profile_and_staff_assignments()
    {
        var (tree, admin, staff) = await SetUpAsync("LC9");
        await AccessTestData.AssignAsync(_factory, tree, tree.School2, BuiltInRoles.Teacher, staff.Summary.UserId); // a share

        var response = await SendAsync(HttpMethod.Delete, Routes.StaffMember(staff.Summary.Id), admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, Routes.StaffMember(staff.Summary.Id), admin)).StatusCode);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var profile = await db.Set<StaffProfile>().IgnoreQueryFilters().SingleAsync(p => p.Id == staff.Summary.Id);
        Assert.True(profile.IsDeleted);
        var assignments = await db.RoleAssignments.IgnoreQueryFilters().Where(a => a.UserId == staff.Summary.UserId).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.All(assignments, a => Assert.True(a.IsDeleted));
        Assert.Empty(await ContextsAsync(staff.Summary.UserId));
    }

    [Fact]
    public async Task Resend_invitation_409_when_already_activated()
    {
        var (_, admin, staff) = await SetUpAsync("LC10");
        var before = _emails.To(staff.Summary.Email).Count;

        var resent = await SendAsync(HttpMethod.Post, Routes.StaffResendInvitation(staff.Summary.Id), admin);

        Assert.Equal(HttpStatusCode.NoContent, resent.StatusCode);
        Assert.Equal(before + 1, _emails.To(staff.Summary.Email).Count);
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.AddPasswordAsync((await users.FindByIdAsync(staff.Summary.UserId.ToString()))!, "Passw0rd1")).Succeeded);
        }

        var activated = await SendAsync(HttpMethod.Post, Routes.StaffResendInvitation(staff.Summary.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, activated.StatusCode);
        Assert.Equal(ProblemCodes.already_activated, await CodeAsync(activated));
        Assert.Equal(before + 1, _emails.To(staff.Summary.Email).Count);
    }

    [Fact]
    public async Task Manager_without_Staff_Update_gets_403()
    {
        var (tree, _, staff) = await SetUpAsync("LC11");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, Routes.StaffMember(staff.Summary.Id), manager, Update(staff))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await StatusAsync(manager, staff.Summary.Id, PersonStatuses.Deactivated)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Delete, Routes.StaffMember(staff.Summary.Id), manager)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, Routes.StaffResendInvitation(staff.Summary.Id), manager)).StatusCode);
    }

    [Fact]
    public async Task Other_subtree_staff_is_404()
    {
        var (tree, _, staff) = await SetUpAsync("LC12");
        var school2Admin = await AccessTestData.AssignAsync(_factory, tree, tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, Routes.StaffMember(staff.Summary.Id), school2Admin, Update(staff))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(school2Admin, staff.Summary.Id, PersonStatuses.Deactivated)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, Routes.StaffMember(staff.Summary.Id), school2Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, Routes.StaffResendInvitation(staff.Summary.Id), school2Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StatusAsync(school2Admin, Guid.NewGuid(), PersonStatuses.Deactivated)).StatusCode);
    }

    /// <summary>A custom role with the given permissions plus everything a Teacher holds (so Teacher targets are within its reach).</summary>
    private async Task<RoleAssignment> HrCallerAsync(AccessTree tree, OrgUnit unit, params string[] extra)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var teacherPermissions = await db.Set<RolePermission>().Where(p => p.RoleId == tree.Roles[BuiltInRoles.Teacher].Id).Select(p => p.Permission).ToListAsync();
        var permissions = teacherPermissions.Concat(extra).Distinct().ToArray();
        var role = new Role(tree.Org.Id, null, "موارد", "HR " + Guid.NewGuid().ToString("N")[..6], RoleBaseKind.Staff);
        db.Add(role);
        db.AddRange(permissions.Select(p => new RolePermission(tree.Org.Id, role.Id, p)));
        var assignment = new RoleAssignment(unit, Guid.NewGuid(), role.Id);
        db.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private async Task<StaffDetailDto> CreateWithRoleAsync(AccessTree tree, RoleAssignment creator, string roleKey, OrgUnit home)
    {
        var created = await SendAsync(HttpMethod.Post, Routes.Staff, creator, new CreateStaffRequest(
            "موظف", "Target", $"tg{Guid.NewGuid():N}"[..14] + "@lms.test", null, null, null, null, null, home.Id, null,
            [new StaffRoleInput(tree.Roles[roleKey].Id, home.Id, null, null)]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<StaffDetailDto>(Web))!;
    }

    [Fact]
    public async Task Deactivate_and_delete_are_refused_when_the_target_holds_a_broader_role_than_the_caller_may_manage()
    {
        var (tree, admin, _) = await SetUpAsync("LC20");
        var hr = await HrCallerAsync(tree, tree.Root, Permissions.Staff.View, Permissions.Staff.Update, Permissions.Staff.Delete);
        var secondAdmin = await CreateWithRoleAsync(tree, admin, BuiltInRoles.OrgAdmin, tree.Root);
        var teacher = await CreateWithRoleAsync(tree, admin, BuiltInRoles.Teacher, tree.School1);

        var deactivate = await StatusAsync(hr, secondAdmin.Summary.Id, PersonStatuses.Deactivated);
        var delete = await SendAsync(HttpMethod.Delete, Routes.StaffMember(secondAdmin.Summary.Id), hr);

        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(deactivate));
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(delete));
        Assert.Equal(PersonStatuses.Active, (await GetAsync(admin, secondAdmin.Summary.Id)).Summary.Status); // nothing changed
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.Equal(1, await db.RoleAssignments.CountAsync(a => a.UserId == secondAdmin.Summary.UserId && !a.IsDeleted));
        }

        // reactivating a broader target is refused too (it switches the same access back on)
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(admin, secondAdmin.Summary.Id, PersonStatuses.Deactivated)).StatusCode);
        var reactivate = await StatusAsync(hr, secondAdmin.Summary.Id, PersonStatuses.Active, "back");
        Assert.Equal(HttpStatusCode.Forbidden, reactivate.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(reactivate));
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(admin, secondAdmin.Summary.Id, PersonStatuses.Active, "back")).StatusCode);

        // a narrower target is fine
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(hr, teacher.Summary.Id, PersonStatuses.Deactivated)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(hr, teacher.Summary.Id, PersonStatuses.Active, "back")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, Routes.StaffMember(teacher.Summary.Id), hr)).StatusCode);

        // an Org Admin may still act on an Org Admin
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(admin, secondAdmin.Summary.Id, PersonStatuses.Deactivated)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, Routes.StaffMember(secondAdmin.Summary.Id), admin)).StatusCode);
    }

    [Fact]
    public async Task A_share_with_a_broader_role_outside_the_callers_scope_also_counts_for_the_guard()
    {
        var (tree, _, teacher) = await SetUpAsync("LC21");
        var hr = await HrCallerAsync(tree, tree.School1, Permissions.Staff.View, Permissions.Staff.Update, Permissions.Staff.Delete);
        await AccessTestData.AssignAsync(_factory, tree, tree.School2, BuiltInRoles.OrgAdmin, teacher.Summary.UserId); // an admin role in the other school

        var deactivate = await StatusAsync(hr, teacher.Summary.Id, PersonStatuses.Deactivated);

        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(deactivate));
    }

    [Fact]
    public async Task Moving_home_needs_Staff_Update_at_the_new_home()
    {
        var (tree, admin, staff) = await SetUpAsync("LC22");
        _host.Services.GetRequiredService<TestPolicyEvaluator>().Block(Permissions.Staff.Update, tree.School2);

        var response = await SendAsync(HttpMethod.Put, Routes.StaffMember(staff.Summary.Id), admin, Update(staff, home: tree.School2.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); // allowed at School1, blocked at School2
        Assert.Equal(tree.School1.Id, (await GetAsync(admin, staff.Summary.Id)).Summary.HomeOrgUnitId);
    }

    [Fact]
    public async Task Delete_keeps_Student_and_Guardian_kind_assignments_of_the_same_person()
    {
        var (tree, admin, staff) = await SetUpAsync("LC23");
        var student = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Student, staff.Summary.UserId);
        var guardian = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Guardian, staff.Summary.UserId);

        var response = await SendAsync(HttpMethod.Delete, Routes.StaffMember(staff.Summary.Id), admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var rows = await db.RoleAssignments.IgnoreQueryFilters().Where(a => a.UserId == staff.Summary.UserId).ToListAsync();
        Assert.False(rows.Single(a => a.Id == student.Id).IsDeleted);
        Assert.False(rows.Single(a => a.Id == guardian.Id).IsDeleted);
        Assert.Single(rows, a => a.IsDeleted); // only the staff assignment went
    }

    [Fact]
    public async Task Delete_recomputes_the_account_status_from_the_remaining_profiles_of_every_client()
    {
        var (_, adminA, a) = await SetUpAsync("LC24A");
        var (treeB, adminB, _) = await SetUpAsync("LC24B");
        // the same person in client B (the account is reused by e-mail)
        var inB = await SendAsync(HttpMethod.Post, Routes.Staff, adminB, new CreateStaffRequest(
            "معلم", "Teacher", a.Summary.Email, null, null, null, null, null, treeB.School1.Id, null,
            [new StaffRoleInput(treeB.Roles[BuiltInRoles.Teacher].Id, treeB.School1.Id, null, null)]));
        Assert.Equal(HttpStatusCode.Created, inB.StatusCode);
        var profileB = (await inB.Content.ReadFromJsonAsync<StaffDetailDto>(Web))!;

        async Task<UserStatus> UserStatusAsync()
        {
            await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            return (await db.Users.SingleAsync(u => u.Id == a.Summary.UserId)).Status;
        }

        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(adminA, a.Summary.Id, PersonStatuses.Deactivated)).StatusCode);
        Assert.Equal(UserStatus.Active, await UserStatusAsync()); // client B's profile is still active

        // deleting B's active profile leaves only A's deactivated one: the account follows
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, Routes.StaffMember(profileB.Summary.Id), adminB)).StatusCode);
        Assert.Equal(UserStatus.Deactivated, await UserStatusAsync());

        // deleting the last profile leaves nothing that blocks sign-in
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, Routes.StaffMember(a.Summary.Id), adminA)).StatusCode);
        Assert.Equal(UserStatus.Active, await UserStatusAsync());
    }
}
