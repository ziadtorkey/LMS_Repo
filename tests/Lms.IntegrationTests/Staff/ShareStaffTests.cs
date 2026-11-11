using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Configuration;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Lms.Shared.Notifications;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Staff;

/// <summary>M2 Task 16 (#164, FR-STF-05): a staff member gets roles at units outside their home subtree.</summary>
[Collection(ApiCollection.Name)]
public sealed class ShareStaffTests(LmsApiFactory factory) : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // The shared factory uses a test policy evaluator; this host uses the real one so role-blocking policies apply.
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RoleAssignment caller, object? body = null) =>
        _host.SendAsync(method, url, caller, body);

    private async Task<(AccessTree Tree, RoleAssignment Root, StaffDetailDto Teacher)> SetUpAsync(string name, Func<AccessTree, Task>? beforeCreate = null)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        if (beforeCreate is not null)
        {
            await beforeCreate(tree);
        }

        var root = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var created = await SendAsync(HttpMethod.Post, Routes.Staff, root, new CreateStaffRequest(
            "معلم", "Teacher", $"sh{Guid.NewGuid():N}"[..14] + "@lms.test", null, null, null, null, null, tree.School1.Id, null,
            [new StaffRoleInput(tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id, null, null)]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (tree, root, (await created.Content.ReadFromJsonAsync<StaffDetailDto>(Web))!);
    }

    private static ShareStaffRequest ShareTo(AccessTree tree, OrgUnit unit, string roleKey = BuiltInRoles.Teacher) =>
        new([new StaffRoleInput(tree.Roles[roleKey].Id, unit.Id, null, null)]);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Root_admin_shares_School1_teacher_to_School2_and_School2_lists_them_as_shared()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR1");
        var school2Admin = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root, ShareTo(tree, tree.School2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var roles = (await response.Content.ReadFromJsonAsync<List<StaffRoleDto>>(Web))!;
        var shared = Assert.Single(roles);
        Assert.Equal(tree.School2.Id, shared.OrgUnitId);
        Assert.True(shared.IsShare);

        var list = await SendAsync(HttpMethod.Get, $"{Routes.Staff}?orgUnitId={tree.School2.Id}&includeShared=true", school2Admin);
        var page = (await list.Content.ReadFromJsonAsync<PagedResult<StaffSummaryDto>>(Web))!;
        var row = Assert.Single(page.Items, s => s.Id == teacher.Summary.Id);
        Assert.True(row.IsSharedIn);
        Assert.Null(row.Mobile); // limited view of a person shared in (D29)
    }

    [Fact]
    public async Task School1_admin_cannot_share_into_School2_404()
    {
        var (tree, _, teacher) = await SetUpAsync("SHR2");
        var school1Admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), school1Admin, ShareTo(tree, tree.School2));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.RoleAssignments.AnyAsync(a => a.UserId == teacher.Summary.UserId && a.OrgUnitId == tree.School2.Id));
    }

    [Fact]
    public async Task CreateTeachers_at_School2_blocks_sharing_a_teacher_there()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR3", async t =>
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            db.Add(new OrgUnitPolicy(t.Org.Id, t.School2.Id, t.School2.Path, PolicyCatalog.CreateTeachers));
            await db.SaveChangesAsync();
        });

        var response = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root, ShareTo(tree, tree.School2));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("BlockedByPolicy", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString());
        // a policy at School2 does not stop sharing a non-Teacher role there
        var counselor = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root, ShareTo(tree, tree.School2, BuiltInRoles.Counselor));
        Assert.Equal(HttpStatusCode.OK, counselor.StatusCode);
    }

    [Fact]
    public async Task Escalation_is_refused()
    {
        var (tree, _, teacher) = await SetUpAsync("SHR4");
        Role limited;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            limited = new Role(tree.Org.Id, null, "محدود", "Limited", RoleBaseKind.Staff);
            db.Add(limited);
            foreach (var permission in new[] { Permissions.Staff.View, Permissions.Staff.Update, Permissions.Assignments.Create })
            {
                db.Add(new RolePermission(tree.Org.Id, limited.Id, permission));
            }

            await db.SaveChangesAsync();
        }

        RoleAssignment caller;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            caller = new RoleAssignment(tree.Root, Guid.NewGuid(), limited.Id);
            db.Add(caller);
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), caller, ShareTo(tree, tree.School2, BuiltInRoles.OrgAdmin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(response));
    }

    [Fact]
    public async Task Overlapping_share_is_409()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR5");
        var url = Routes.StaffShares(teacher.Summary.Id);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, url, root, ShareTo(tree, tree.School2))).StatusCode);

        var again = await SendAsync(HttpMethod.Post, url, root, ShareTo(tree, tree.School2));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ProblemCodes.assignment_exists, await CodeAsync(again));
    }

    [Fact]
    public async Task All_targets_are_shared_or_none_is()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR6");
        var body = new ShareStaffRequest([
            new StaffRoleInput(tree.Roles[BuiltInRoles.Teacher].Id, tree.School2.Id, null, null),
            new StaffRoleInput(tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id, null, null), // already has this one
        ]);

        var response = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.RoleAssignments.AnyAsync(a => a.UserId == teacher.Summary.UserId && a.OrgUnitId == tree.School2.Id));
    }

    [Fact]
    public async Task Target_inside_the_home_subtree_is_allowed_and_is_not_a_share()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR7");

        var response = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root,
            ShareTo(tree, tree.School1, BuiltInRoles.Counselor));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(Assert.Single((await response.Content.ReadFromJsonAsync<List<StaffRoleDto>>(Web))!).IsShare);
    }

    [Fact]
    public async Task Removing_share_removes_context()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR8");
        var shared = (await (await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root, ShareTo(tree, tree.School2)))
            .Content.ReadFromJsonAsync<List<StaffRoleDto>>(Web))!.Single();

        async Task<MeDto> MeAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Routes.Me);
            request.Headers.Add(TestAuthHandler.UserHeader, teacher.Summary.UserId.ToString());
            return (await (await factory.CreateClient().SendAsync(request)).Content.ReadFromJsonAsync<MeDto>(Web))!;
        }

        Assert.Contains((await MeAsync()).Contexts, c => c.AssignmentId == shared.AssignmentId);

        var removed = await SendAsync(HttpMethod.Delete, Routes.StaffShare(teacher.Summary.Id, shared.AssignmentId), root);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain((await MeAsync()).Contexts, c => c.AssignmentId == shared.AssignmentId);
        // someone else's assignment id under this person is not theirs to delete
        var other = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Teacher, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, Routes.StaffShare(teacher.Summary.Id, other.Id), root)).StatusCode);
    }

    [Fact]
    public async Task Share_is_audited_and_notifies_assignee()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR9");

        var response = await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root, ShareTo(tree, tree.School2));
        var shared = (await response.Content.ReadFromJsonAsync<List<StaffRoleDto>>(Web))!.Single();

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.AuditEntries.IgnoreQueryFilters().AnyAsync(e =>
            e.EntityType == nameof(RoleAssignment) && e.EntityId == shared.AssignmentId && e.Action == AuditAction.Created));
        var notification = await db.Set<Notification>().SingleAsync(n =>
            n.UserId == teacher.Summary.UserId && n.EventKey == NotificationEvents.AccessRoleAssigned);
        Assert.Equal(tree.Org.Id, notification.OrganizationId);

        await SendAsync(HttpMethod.Delete, Routes.StaffShare(teacher.Summary.Id, shared.AssignmentId), root);
        Assert.True(await db.Set<Notification>().AnyAsync(n =>
            n.UserId == teacher.Summary.UserId && n.EventKey == NotificationEvents.AccessRoleRemoved));
    }

    [Fact]
    public async Task Unknown_or_other_client_person_is_404_and_validation_is_400()
    {
        var (tree, root, teacher) = await SetUpAsync("SHR10");
        var (otherTree, otherRoot, _) = await SetUpAsync("SHR10B");

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), otherRoot, ShareTo(otherTree, otherTree.School2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, Routes.StaffShares(Guid.NewGuid()), root, ShareTo(tree, tree.School2))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root, new ShareStaffRequest([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, Routes.StaffShares(teacher.Summary.Id), root,
            new ShareStaffRequest([new StaffRoleInput(tree.Roles[BuiltInRoles.Student].Id, tree.School2.Id, null, null)]))).StatusCode);
    }
}
