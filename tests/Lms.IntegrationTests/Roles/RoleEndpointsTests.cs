using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Roles;

[Collection(ApiCollection.Name)]
public sealed class RoleEndpointsTests(LmsApiFactory factory)
{
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, RoleAssignment caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> AdminAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin);
    }

    private static CreateRoleRequest NewRole(string en = "Exam Officer", params string[] permissions) =>
        new("مسؤول الامتحانات " + en, en, RoleBaseKinds.Staff, permissions.Length == 0 ? [Permissions.Roles.View] : permissions);

    private async Task<List<RoleDto>> ListAsync(RoleAssignment caller) =>
        (await (await SendAsync(HttpMethod.Get, Routes.Roles, caller)).Content.ReadFromJsonAsync<List<RoleDto>>())!;

    [Fact]
    public async Task Root_admin_creates_custom_Exam_Officer_role_with_base_kind()
    {
        var (_, admin) = await AdminAsync("RL1");

        var response = await SendAsync(HttpMethod.Post, Routes.Roles, admin,
            NewRole("Exam Officer", Permissions.Roles.View, Permissions.Audit.View));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var role = (await response.Content.ReadFromJsonAsync<RoleDto>())!;
        Assert.False(role.IsBuiltIn);
        Assert.Null(role.BuiltInKey);
        Assert.Equal(RoleBaseKinds.Staff, role.BaseKind);
        Assert.Equal([Permissions.Audit.View, Permissions.Roles.View], role.Permissions.Order().ToArray());

        var list = await ListAsync(admin);
        Assert.Contains(list, r => r.Id == role.Id);
        Assert.Equal(8, list.Count);
    }

    [Fact]
    public async Task Permissions_catalog_excludes_platform_only()
    {
        var (_, admin) = await AdminAsync("RL2");

        var groups = (await (await SendAsync(HttpMethod.Get, Routes.Permissions, admin))
            .Content.ReadFromJsonAsync<List<PermissionGroupDto>>())!;

        Assert.DoesNotContain(groups, g => g.Module == "Clients");
        Assert.Contains(groups, g => g.Module == "Roles");
    }

    [Fact]
    public async Task Built_in_role_label_and_permissions_are_editable_per_client_only()
    {
        var (tree, admin) = await AdminAsync("RL3");
        var other = await AccessTestData.SeedAsync(factory, "RL3B");
        var teacher = tree.Roles[BuiltInRoles.Teacher];

        var response = await SendAsync(HttpMethod.Put, Routes.Role(teacher.Id), admin,
            new UpdateRoleRequest("معلّم أول", "Lead teacher", [Permissions.Audit.View]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<RoleDto>())!;
        Assert.Equal("Lead teacher", updated.NameEn);
        Assert.Equal(RoleBaseKinds.Staff, updated.BaseKind);
        Assert.Equal(BuiltInRoles.Teacher, updated.BuiltInKey);
        Assert.Equal([Permissions.Audit.View], updated.Permissions);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var otherTeacher = await db.Roles.SingleAsync(r => r.Id == other.Roles[BuiltInRoles.Teacher].Id);
        Assert.NotEqual("Lead teacher", otherTeacher.NameEn);

        // A role of another client is invisible to this admin.
        var foreign = await SendAsync(HttpMethod.Put, Routes.Role(otherTeacher.Id), admin,
            new UpdateRoleRequest("x", "x", [Permissions.Audit.View]));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Custom_role_with_platform_only_permission_is_400()
    {
        var (_, admin) = await AdminAsync("RL4");

        var response = await SendAsync(HttpMethod.Post, Routes.Roles, admin, NewRole("Hacker", Permissions.Clients.Create));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.platform_only_permission, await CodeAsync(response));
    }

    [Fact]
    public async Task Granting_permission_caller_lacks_is_403_permission_escalation()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RL5");
        var userId = Guid.NewGuid();
        RoleAssignment caller;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var limited = new Role(tree.Org.Id, null, "محدود", "Limited", RoleBaseKind.Staff);
            db.Roles.Add(limited);
            db.RolePermissions.AddRange(
                new RolePermission(tree.Org.Id, limited.Id, Permissions.Roles.View),
                new RolePermission(tree.Org.Id, limited.Id, Permissions.Roles.Create));
            caller = new RoleAssignment(tree.Root, userId, limited.Id);
            db.RoleAssignments.Add(caller);
            await db.SaveChangesAsync();
        }

        var ok = await SendAsync(HttpMethod.Post, Routes.Roles, caller, NewRole("Fine", Permissions.Roles.View));
        var escalate = await SendAsync(HttpMethod.Post, Routes.Roles, caller,
            NewRole("Greedy", Permissions.Roles.View, Permissions.Audit.View));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(escalate));
    }

    [Fact]
    public async Task Removing_Roles_Update_from_OrgAdmin_is_409()
    {
        var (tree, admin) = await AdminAsync("RL6");
        var orgAdmin = (await ListAsync(admin)).Single(r => r.Id == tree.Roles[BuiltInRoles.OrgAdmin].Id);

        var response = await SendAsync(HttpMethod.Put, Routes.Role(orgAdmin.Id), admin,
            new UpdateRoleRequest(orgAdmin.NameAr, orgAdmin.NameEn,
                orgAdmin.Permissions.Where(p => p != Permissions.Roles.Update).ToList()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.last_admin_permission, await CodeAsync(response));
    }

    [Fact]
    public async Task School_admin_cannot_create_roles_OutOfScope()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RL7");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var create = await SendAsync(HttpMethod.Post, Routes.Roles, schoolAdmin, NewRole());
        var edit = await SendAsync(HttpMethod.Put, Routes.Role(tree.Roles[BuiltInRoles.Teacher].Id), schoolAdmin,
            new UpdateRoleRequest("a", "b", [Permissions.Roles.View]));

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("OutOfScope", (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_create_roles()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RL8");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, Routes.Roles, teacher, NewRole())).StatusCode);
    }

    [Fact]
    public async Task Delete_built_in_is_409_and_in_use_custom_is_409_and_unused_custom_is_deleted()
    {
        var (tree, admin) = await AdminAsync("RL9");

        var builtIn = await SendAsync(HttpMethod.Delete, Routes.Role(tree.Roles[BuiltInRoles.Teacher].Id), admin);
        Assert.Equal(HttpStatusCode.Conflict, builtIn.StatusCode);
        Assert.Equal(ProblemCodes.built_in_role, await CodeAsync(builtIn));

        var inUse = (await (await SendAsync(HttpMethod.Post, Routes.Roles, admin, NewRole("In use"))).Content.ReadFromJsonAsync<RoleDto>())!;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.RoleAssignments.Add(new RoleAssignment(tree.School1, Guid.NewGuid(), inUse.Id));
            await db.SaveChangesAsync();
        }

        var used = await SendAsync(HttpMethod.Delete, Routes.Role(inUse.Id), admin);
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
        Assert.Equal(ProblemCodes.role_in_use, await CodeAsync(used));

        var free = (await (await SendAsync(HttpMethod.Post, Routes.Roles, admin, NewRole("Free"))).Content.ReadFromJsonAsync<RoleDto>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, Routes.Role(free.Id), admin)).StatusCode);
        Assert.DoesNotContain(await ListAsync(admin), r => r.Id == free.Id);
    }

    [Fact]
    public async Task Duplicate_role_name_is_409_name_taken()
    {
        var (_, admin) = await AdminAsync("RL10");
        await SendAsync(HttpMethod.Post, Routes.Roles, admin, NewRole("Unique"));

        var response = await SendAsync(HttpMethod.Post, Routes.Roles, admin, NewRole("Unique"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.name_taken, await CodeAsync(response));
    }

    [Fact]
    public async Task Blank_names_bad_kind_and_unknown_permission_are_400()
    {
        var (_, admin) = await AdminAsync("RL11");

        var blank = await SendAsync(HttpMethod.Post, Routes.Roles, admin,
            new CreateRoleRequest("  ", "x", RoleBaseKinds.Staff, [Permissions.Roles.View]));
        var badKind = await SendAsync(HttpMethod.Post, Routes.Roles, admin,
            new CreateRoleRequest("ا", "x", "Alien", [Permissions.Roles.View]));
        var unknown = await SendAsync(HttpMethod.Post, Routes.Roles, admin, NewRole("Weird", "Nope.Nothing"));

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Removed_permission_is_denied_on_next_request()
    {
        var (tree, admin) = await AdminAsync("RL12");
        var role = (await (await SendAsync(HttpMethod.Post, Routes.Roles, admin, NewRole("Auditor", Permissions.Audit.View)))
            .Content.ReadFromJsonAsync<RoleDto>())!;
        var auditor = new RoleAssignment(tree.Root, Guid.NewGuid(), role.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.RoleAssignments.Add(auditor);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/audit/", auditor)).StatusCode);

        var update = await SendAsync(HttpMethod.Put, Routes.Role(role.Id), admin,
            new UpdateRoleRequest(role.NameAr, role.NameEn, [Permissions.Roles.View]));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/test/audit/", auditor)).StatusCode);
    }

    [Fact]
    public async Task Role_permission_change_is_audited()
    {
        var (tree, admin) = await AdminAsync("RL13");
        var teacher = tree.Roles[BuiltInRoles.Teacher];

        await SendAsync(HttpMethod.Put, Routes.Role(teacher.Id), admin,
            new UpdateRoleRequest(teacher.NameAr, teacher.NameEn, [Permissions.Audit.View]));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries
            .Where(e => e.OrganizationId == tree.Org.Id && e.EntityType == nameof(RolePermission) && e.UserId == admin.UserId)
            .ToListAsync();
        Assert.Contains(entries, e => e.Action == AuditAction.Created && e.AfterJson!.Contains(Permissions.Audit.View));
        Assert.Contains(entries, e => e.Action == AuditAction.Deleted);
    }
}
