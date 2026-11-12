using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Assignments;

[Collection(ApiCollection.Name)]
public sealed class AssignmentEndpointsTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;
    private readonly FakeEmailSender _emails = new();

    public AssignmentEndpointsTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_emails)));
    }

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

        return await _host.CreateClient().SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<User> CreateUserAsync(OrgUnit home, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@lms.test";
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var user = new User { UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayNameAr = "مستخدم", DisplayNameEn = "Some User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await PeopleTestKit.AddStaffAsync(_factory, home, email, userId: user.Id); // roles go to existing staff only (D31)
        return user;
    }

    private async Task<(AccessTree Tree, RoleAssignment Admin)> AdminAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin);
    }

    private static CreateAssignmentRequest Request(
        string email, Guid roleId, Guid unitId, DateOnly? from = null, DateOnly? to = null) =>
        new(email, "اسم", "Name", roleId, unitId, from, to);

    private async Task<AssignmentDto> CreateOkAsync(RoleAssignment caller, CreateAssignmentRequest request)
    {
        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, caller, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AssignmentDto>())!;
    }

    [Fact]
    public async Task Admin_assigns_existing_user_teacher_role_at_section()
    {
        var (tree, admin) = await AdminAsync("AS1");
        var user = await CreateUserAsync(tree.School1);

        var dto = await CreateOkAsync(admin, Request(user.Email!, tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id));

        Assert.Equal(user.Id, dto.UserId);
        Assert.Equal(tree.School1.Id, dto.OrgUnitId);
        Assert.True(dto.IsCurrentlyValid);
        Assert.Empty(_emails.To(user.Email!));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var saved = await db.RoleAssignments.SingleAsync(a => a.Id == dto.Id);
        Assert.Equal(tree.School1.Path, saved.OrgUnitPath);
    }

    [Fact]
    public async Task Unknown_email_is_400_person_not_found_and_creates_no_user()
    {
        var (tree, admin) = await AdminAsync("AS2");
        var email = $"new-{Guid.NewGuid():N}@lms.test";

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            Request(email, tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.person_not_found, await CodeAsync(response));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        Assert.Empty(_emails.To(email));
    }

    [Fact]
    public async Task Email_of_staff_in_sibling_subtree_is_400_not_revealing()
    {
        var (tree, _) = await AdminAsync("AS3");
        var school1Admin = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var sibling = await CreateUserAsync(tree.School2); // staff exists, but at School2: outside the School1 admin's scope
        var other = await AccessTestData.SeedAsync(_factory, "AS3B");
        var elsewhere = await CreateUserAsync(other.School1); // staff of another client
        var unknown = $"nobody-{Guid.NewGuid():N}@lms.test";
        var role = tree.Roles[BuiltInRoles.Teacher].Id;

        var asSibling = await SendAsync(HttpMethod.Post, Routes.Assignments, school1Admin, Request(sibling.Email!, role, tree.School1.Id));
        var asOtherClient = await SendAsync(HttpMethod.Post, Routes.Assignments, school1Admin, Request(elsewhere.Email!, role, tree.School1.Id));
        var asUnknown = await SendAsync(HttpMethod.Post, Routes.Assignments, school1Admin, Request(unknown, role, tree.School1.Id));

        // Identical answers: the caller cannot tell "exists elsewhere" from "does not exist".
        foreach (var response in new[] { asSibling, asOtherClient, asUnknown })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.person_not_found, await CodeAsync(response));
        }
    }

    [Fact]
    public async Task Invalid_email_is_400()
    {
        var (tree, admin) = await AdminAsync("AS4");

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            Request("not-an-email", tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assigning_OrgAdmin_role_by_caller_without_its_permissions_is_403()
    {
        var (tree, _) = await AdminAsync("AS5");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var user = await CreateUserAsync(tree.School1);

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, manager,
            Request(user.Email!, tree.Roles[BuiltInRoles.OrgAdmin].Id, tree.School1.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(response));
    }

    [Fact]
    public async Task Manager_cannot_assign_Manager()
    {
        var (tree, _) = await AdminAsync("AS6");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var user = await CreateUserAsync(tree.School1);

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, manager,
            Request(user.Email!, tree.Roles[BuiltInRoles.Manager].Id, tree.School1.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(response));
    }

    [Fact]
    public async Task Manager_cannot_assign_OrgAdmin()
    {
        var (tree, _) = await AdminAsync("AS7");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var user = await CreateUserAsync(tree.School1);

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, manager,
            Request(user.Email!, tree.Roles[BuiltInRoles.OrgAdmin].Id, tree.Root.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_can_assign_Teacher()
    {
        var (tree, _) = await AdminAsync("AS8");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var user = await CreateUserAsync(tree.School1);

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, manager,
            Request(user.Email!, tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OrgAdmin_can_assign_OrgAdmin()
    {
        var (tree, admin) = await AdminAsync("AS9");
        var user = await CreateUserAsync(tree.School1);

        await CreateOkAsync(admin, Request(user.Email!, tree.Roles[BuiltInRoles.OrgAdmin].Id, tree.School1.Id));
    }

    [Fact]
    public async Task Role_of_another_client_is_404()
    {
        var (tree, admin) = await AdminAsync("AS10");
        var other = await AccessTestData.SeedAsync(_factory, "AS10B");
        var user = await CreateUserAsync(tree.School1);

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            Request(user.Email!, other.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Assignment_in_other_subtree_unit_is_404()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "AS11");
        var schoolAdmin = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var user = await CreateUserAsync(tree.School1);

        var create = await SendAsync(HttpMethod.Post, Routes.Assignments, schoolAdmin,
            Request(user.Email!, tree.Roles[BuiltInRoles.Teacher].Id, tree.School2.Id));
        var list = await SendAsync(HttpMethod.Get, Routes.OrgUnitAssignments(tree.School2.Id), schoolAdmin);

        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.RoleAssignments.AnyAsync(a => a.UserId == user.Id));
    }

    [Fact]
    public async Task Overlapping_duplicate_is_409_but_disjoint_validity_is_fine()
    {
        var (tree, admin) = await AdminAsync("AS12");
        var user = await CreateUserAsync(tree.School1);
        var roleId = tree.Roles[BuiltInRoles.Teacher].Id;
        await CreateOkAsync(admin, Request(user.Email!, roleId, tree.School1.Id, Today, Today.AddDays(10)));

        var overlap = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            Request(user.Email!, roleId, tree.School1.Id, Today.AddDays(5), null));
        var disjoint = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            Request(user.Email!, roleId, tree.School1.Id, Today.AddDays(11), null));
        var otherUnit = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            Request(user.Email!, roleId, tree.School2.Id, Today, null));

        Assert.True(overlap.StatusCode == HttpStatusCode.Conflict, await overlap.Content.ReadAsStringAsync());
        Assert.Equal(ProblemCodes.assignment_exists, await CodeAsync(overlap));
        Assert.True(disjoint.StatusCode == HttpStatusCode.Created, await disjoint.Content.ReadAsStringAsync());
        Assert.True(otherUnit.StatusCode == HttpStatusCode.Created, await otherUnit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ValidTo_before_ValidFrom_is_400()
    {
        var (tree, admin) = await AdminAsync("AS13");
        var user = await CreateUserAsync(tree.School1);

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            Request(user.Email!, tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id, Today, Today.AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updated_validity_controls_context_validity()
    {
        var (tree, admin) = await AdminAsync("AS14");
        var user = await CreateUserAsync(tree.School1);
        var dto = await CreateOkAsync(admin, Request(user.Email!, tree.Roles[BuiltInRoles.Supervisor].Id, tree.School1.Id));
        RoleAssignment assignee;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            assignee = await db.RoleAssignments.SingleAsync(a => a.Id == dto.Id);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/org-units/view", assignee)).StatusCode);

        var expired = await SendAsync(HttpMethod.Put, Routes.Assignment(dto.Id), admin,
            new UpdateAssignmentRequest(Today.AddDays(-5), Today.AddDays(-1)));

        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        Assert.False((await expired.Content.ReadFromJsonAsync<AssignmentDto>())!.IsCurrentlyValid);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/test/org-units/view", assignee)).StatusCode);

        var renewed = await SendAsync(HttpMethod.Put, Routes.Assignment(dto.Id), admin, new UpdateAssignmentRequest(null, null));
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/org-units/view", assignee)).StatusCode);

        var inverted = await SendAsync(HttpMethod.Put, Routes.Assignment(dto.Id), admin,
            new UpdateAssignmentRequest(Today, Today.AddDays(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
    }

    [Fact]
    public async Task Removing_own_assignment_is_409()
    {
        var (_, admin) = await AdminAsync("AS15");

        var response = await SendAsync(HttpMethod.Delete, Routes.Assignment(admin.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.own_assignment, await CodeAsync(response));
    }

    [Fact]
    public async Task Delete_removes_the_assignment_and_is_audited_with_create()
    {
        var (tree, admin) = await AdminAsync("AS16");
        var user = await CreateUserAsync(tree.School1);
        var dto = await CreateOkAsync(admin, Request(user.Email!, tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, Routes.Assignment(dto.Id), admin)).StatusCode);

        var list = (await (await SendAsync(HttpMethod.Get, Routes.OrgUnitAssignments(tree.School1.Id), admin))
            .Content.ReadFromJsonAsync<PagedResult<AssignmentDto>>())!;
        Assert.DoesNotContain(list.Items, a => a.Id == dto.Id);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.Where(e => e.EntityId == dto.Id && e.EntityType == nameof(RoleAssignment))
            .OrderBy(e => e.TimestampUtc).ToListAsync();
        Assert.Contains(entries, e => e.Action == AuditAction.Created && e.UserId == admin.UserId);
        Assert.Contains(entries, e => e.Action == AuditAction.Deleted && e.UserId == admin.UserId);
    }

    [Fact]
    public async Task Manager_cannot_remove_an_OrgAdmin_assignment()
    {
        var (tree, admin) = await AdminAsync("AS17");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var otherAdmin = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Delete, Routes.Assignment(otherAdmin.Id), manager);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(admin);
    }

    [Fact]
    public async Task Teacher_cannot_assign_or_list()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "AS18");
        var teacher = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());
        var user = await CreateUserAsync(tree.School1);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, Routes.Assignments, teacher,
            Request(user.Email!, tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(HttpMethod.Get, Routes.OrgUnitAssignments(tree.Root.Id), teacher)).StatusCode);
    }

    [Fact]
    public async Task List_with_descendants_is_paged()
    {
        var (tree, admin) = await AdminAsync("AS19");
        var roleId = tree.Roles[BuiltInRoles.Teacher].Id;
        for (var i = 0; i < 3; i++)
        {
            await CreateOkAsync(admin, Request((await CreateUserAsync(tree.School1)).Email!, roleId, tree.School1.Id));
        }

        await CreateOkAsync(admin, Request((await CreateUserAsync(tree.School1)).Email!, roleId, tree.School2.Id));

        async Task<PagedResult<AssignmentDto>> Get(string query) => (await (await SendAsync(
            HttpMethod.Get, Routes.OrgUnitAssignments(tree.Root.Id) + query, admin)).Content
            .ReadFromJsonAsync<PagedResult<AssignmentDto>>())!;

        var direct = await Get(string.Empty);
        Assert.Equal(0, direct.Total); // the assignments sit below the root
        var all = await Get("?includeDescendants=true&page=1&pageSize=3");
        Assert.Equal(4, all.Total);
        Assert.Equal(3, all.Items.Count);
        Assert.Equal(3, all.PageSize);
        var second = await Get("?includeDescendants=true&page=2&pageSize=3");
        Assert.Single(second.Items);
        var school = await (await SendAsync(HttpMethod.Get, Routes.OrgUnitAssignments(tree.School1.Id), admin))
            .Content.ReadFromJsonAsync<PagedResult<AssignmentDto>>();
        Assert.Equal(3, school!.Total);
        Assert.All(school.Items, a => Assert.Equal("Some User", a.UserDisplayNameEn));
    }

    [Fact]
    public async Task Invalid_paging_is_400()
    {
        var (tree, admin) = await AdminAsync("AS20");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Get, Routes.OrgUnitAssignments(tree.Root.Id) + "?page=0", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Get, Routes.OrgUnitAssignments(tree.Root.Id) + "?pageSize=201", admin)).StatusCode);
    }
}
