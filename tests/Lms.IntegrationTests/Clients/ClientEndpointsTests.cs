using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Abstractions;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Clients;

[Collection(ApiCollection.Name)]
public sealed class ClientEndpointsTests(LmsApiFactory factory) : IDisposable
{
    private readonly FakeEmailSender _emails = new();
    private WebApplicationFactory<Program>? _host;

    private HttpClient Api() =>
        (_host ??= factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_emails)))).CreateClient();

    public void Dispose() => _host?.Dispose();

    private static CreateClientRequest NewClient(string tag, string type = "SchoolGroup", string? adminEmail = null) => new(
        NameAr: "مدارس " + tag, NameEn: "Schools " + tag, Type: type,
        RootLevelNameAr: "مدرسة", RootLevelNameEn: "School", RootUnitCode: "ROOT-" + tag,
        AdminEmail: adminEmail ?? $"admin-{tag}-{Guid.NewGuid():N}@clients.test".ToLowerInvariant(),
        AdminDisplayNameAr: "مدير " + tag, AdminDisplayNameEn: "Admin " + tag);

    private static HttpRequestMessage Request(HttpMethod method, string url, object? body, Guid? user, bool platformAdmin,
        Guid? assignment = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        }

        if (platformAdmin)
        {
            request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        }

        if (assignment is not null)
        {
            request.Headers.Add(ContextHeader.Name, assignment.ToString());
        }

        return request;
    }

    private Task<HttpResponseMessage> AsPlatformAdminAsync(HttpMethod method, string url, object? body = null) =>
        Api().SendAsync(Request(method, url, body, Guid.NewGuid(), platformAdmin: true));

    private async Task<ClientDto> CreateAsync(CreateClientRequest request)
    {
        var response = await AsPlatformAdminAsync(HttpMethod.Post, Routes.Clients, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClientDto>())!;
    }

    private Lms.Infrastructure.Persistence.LmsDbContext Admin() => factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

    [Fact]
    public async Task Platform_admin_creates_school_group_with_root_unit_roles_and_org_admin()
    {
        var request = NewClient("A1");

        var response = await AsPlatformAdminAsync(HttpMethod.Post, Routes.Clients, request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ClientDto>())!;
        Assert.Equal(request.NameEn, dto.NameEn);
        Assert.Equal(request.NameAr, dto.NameAr);
        Assert.Equal("SchoolGroup", dto.Type);
        Assert.Equal("Active", dto.Status);
        Assert.Equal($"{Routes.Clients}/{dto.Id}", response.Headers.Location?.ToString());

        await using var db = Admin();
        var root = await db.OrgUnits.SingleAsync(u => u.OrganizationId == dto.Id);
        Assert.Equal(dto.RootOrgUnitId, root.Id);
        Assert.Equal(request.RootUnitCode, root.Code);
        Assert.Null(root.ParentId);
        var level = await db.LevelTypes.SingleAsync(l => l.OrganizationId == dto.Id);
        Assert.Equal((1, false, request.RootLevelNameEn), (level.Order, level.CanHostClasses, level.NameEn));
        Assert.Equal(root.LevelTypeId, level.Id);
        Assert.Equal(7, await db.Roles.CountAsync(r => r.OrganizationId == dto.Id && r.IsBuiltIn));

        var user = await db.Users.SingleAsync(x => x.Email == request.AdminEmail);
        Assert.Equal((request.AdminDisplayNameAr, request.AdminDisplayNameEn), (user.DisplayNameAr, user.DisplayNameEn));
        Assert.Null(user.PasswordHash);
        var assignment = await db.RoleAssignments.SingleAsync(a => a.UserId == user.Id);
        Assert.Equal(root.Id, assignment.OrgUnitId);
        Assert.Equal(BuiltInRoles.OrgAdmin, (await db.Roles.SingleAsync(r => r.Id == assignment.RoleId)).BuiltInKey);
    }

    [Fact]
    public async Task University_client_gets_university_role_labels()
    {
        var dto = await CreateAsync(NewClient("U1", "University"));

        await using var db = Admin();
        var manager = await db.Roles.SingleAsync(r => r.OrganizationId == dto.Id && r.BuiltInKey == BuiltInRoles.Manager);
        Assert.Equal("Dean", manager.NameEn);
        Assert.Equal("University", dto.Type);
    }

    [Fact]
    public async Task New_admin_receives_invitation_email()
    {
        var request = NewClient("E1");

        await CreateAsync(request);

        var mail = Assert.Single(_emails.To(request.AdminEmail));
        Assert.False(string.IsNullOrWhiteSpace(mail.Subject));
    }

    [Fact]
    public async Task Existing_user_email_is_reused_and_gets_no_invitation()
    {
        var email = $"existing-{Guid.NewGuid():N}@clients.test";
        Guid existingId;
        await using (var scope = (_host ??= factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_emails)))).Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = new User { UserName = email, Email = email, DisplayNameAr = "قديم", DisplayNameEn = "Old" };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            existingId = user.Id;
        }

        var dto = await CreateAsync(NewClient("E2", adminEmail: email));

        await using var db = Admin();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == email));
        Assert.Equal(existingId, (await db.RoleAssignments.SingleAsync(a => a.OrganizationId == dto.Id)).UserId);
        Assert.Equal("Old", (await db.Users.SingleAsync(u => u.Id == existingId)).DisplayNameEn);
        Assert.Empty(_emails.To(email));
    }

    [Fact]
    public async Task A_failing_mail_server_does_not_undo_the_created_client()
    {
        _emails.Fail = true;
        var request = NewClient("E3");

        var dto = await CreateAsync(request);

        await using var db = Admin();
        Assert.True(await db.Organizations.AnyAsync(o => o.Id == dto.Id));
    }

    [Fact]
    public async Task Org_admin_cannot_list_or_create_clients_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, Routes.Clients, null),
                     (HttpMethod.Post, Routes.Clients, NewClient("X1")),
                     (HttpMethod.Get, Routes.Client(tree.Org.Id), null),
                     (HttpMethod.Put, Routes.Client(tree.Org.Id), new UpdateClientRequest("ا", "A", "Active")),
                 })
        {
            var response = await Api().SendAsync(Request(method, url, body, admin.UserId, false, admin.Id));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Forged_role_row_holding_a_clients_permission_still_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await using (var db = Admin())
        {
            foreach (var permission in Permissions.PlatformOnly)
            {
                db.RolePermissions.Add(new RolePermission(tree.Org.Id, tree.Roles[BuiltInRoles.OrgAdmin].Id, permission));
            }

            await db.SaveChangesAsync();
        }

        var list = await Api().SendAsync(Request(HttpMethod.Get, Routes.Clients, null, admin.UserId, false, admin.Id));
        var create = await Api().SendAsync(Request(HttpMethod.Post, Routes.Clients, NewClient("X2"), admin.UserId, false, admin.Id));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_gets_401()
    {
        var response = await Api().SendAsync(Request(HttpMethod.Get, Routes.Clients, null, null, false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Client_list_is_paged_and_searchable_in_arabic()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var arabicName = $"مدارس {tag}";
        for (var i = 0; i < 3; i++)
        {
            var request = NewClient($"{tag}-{i}") with { NameAr = $"{arabicName} {i}", NameEn = $"Zeta {tag} {i}" };
            await CreateAsync(request);
        }

        await CreateAsync(NewClient("other-" + tag) with { NameAr = "جامعة مختلفة " + tag, NameEn = "Other " + tag });

        var page1 = await GetPageAsync($"{Routes.Clients}?page=1&pageSize=2&search={Uri.EscapeDataString(arabicName)}");
        var page2 = await GetPageAsync($"{Routes.Clients}?page=2&pageSize=2&search={Uri.EscapeDataString(arabicName)}");
        var english = await GetPageAsync($"{Routes.Clients}?search={Uri.EscapeDataString("Zeta " + tag)}");
        var none = await GetPageAsync($"{Routes.Clients}?search=no-such-client-{tag}");

        Assert.Equal((3, 2, 1), (page1.Total, page1.Items.Count, page2.Items.Count));
        Assert.Equal((1, 2), (page1.Page, page1.PageSize));
        Assert.All(page1.Items.Concat(page2.Items), c => Assert.Contains(arabicName, c.NameAr));
        Assert.Equal(3, english.Total);
        Assert.Equal(0, none.Total);
    }

    private async Task<PagedResult<ClientDto>> GetPageAsync(string url)
    {
        var response = await AsPlatformAdminAsync(HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ClientDto>>())!;
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    public async Task Invalid_paging_returns_validation_problem(string query)
    {
        var response = await AsPlatformAdminAsync(HttpMethod.Get, $"{Routes.Clients}?{query}");

        await AssertValidationProblemAsync(response);
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ProblemCodes.validation_failed, body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Type", "School")]
    [InlineData("RootUnitCode", "bad code!")]
    [InlineData("RootUnitCode", "")]
    [InlineData("NameEn", "   ")]
    [InlineData("NameAr", "")]
    [InlineData("AdminEmail", "not-an-email")]
    [InlineData("AdminDisplayNameEn", "")]
    [InlineData("RootLevelNameAr", "")]
    public async Task Invalid_type_or_code_returns_validation_problem(string field, string value)
    {
        var request = NewClient("V1");
        var invalid = field switch
        {
            "Type" => request with { Type = value },
            "RootUnitCode" => request with { RootUnitCode = value },
            "NameEn" => request with { NameEn = value },
            "NameAr" => request with { NameAr = value },
            "AdminEmail" => request with { AdminEmail = value },
            "AdminDisplayNameEn" => request with { AdminDisplayNameEn = value },
            _ => request with { RootLevelNameAr = value },
        };

        var response = await AsPlatformAdminAsync(HttpMethod.Post, Routes.Clients, invalid);

        await AssertValidationProblemAsync(response);
        await using var db = Admin();
        Assert.False(await db.Organizations.AnyAsync(o => o.NameEn == "Schools V1"));
    }

    [Fact]
    public async Task Get_and_update_a_client_and_unknown_id_is_404()
    {
        var dto = await CreateAsync(NewClient("G1"));

        var get = await AsPlatformAdminAsync(HttpMethod.Get, Routes.Client(dto.Id));
        var put = await AsPlatformAdminAsync(HttpMethod.Put, Routes.Client(dto.Id), new UpdateClientRequest("اسم جديد", "New name", "Suspended"));
        var missing = await AsPlatformAdminAsync(HttpMethod.Get, Routes.Client(Guid.NewGuid()));
        var badStatus = await AsPlatformAdminAsync(HttpMethod.Put, Routes.Client(dto.Id), new UpdateClientRequest("ا", "A", "Gone"));

        Assert.Equal(dto.NameEn, (await get.Content.ReadFromJsonAsync<ClientDto>())!.NameEn);
        var updated = (await put.Content.ReadFromJsonAsync<ClientDto>())!;
        Assert.Equal(("اسم جديد", "New name", "Suspended"), (updated.NameAr, updated.NameEn, updated.Status));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await AssertValidationProblemAsync(badStatus);
    }

    [Fact]
    public async Task Suspended_client_contexts_are_rejected_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SU1");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, user);
        var before = await Api().SendAsync(Request(HttpMethod.Get, "/test/scoped", null, user, false, assignment.Id));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var suspend = await AsPlatformAdminAsync(HttpMethod.Put, Routes.Client(tree.Org.Id),
            new UpdateClientRequest(tree.Org.NameAr, tree.Org.NameEn, "Suspended"));
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        var after = await Api().SendAsync(Request(HttpMethod.Get, "/test/scoped", null, user, false, assignment.Id));
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        var body = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_context", body.GetProperty("code").GetString());

        var me = (await (await Api().SendAsync(Request(HttpMethod.Get, Routes.Me, null, user, false))).Content.ReadFromJsonAsync<MeDto>())!;
        Assert.Empty(me.Contexts);

        await AsPlatformAdminAsync(HttpMethod.Put, Routes.Client(tree.Org.Id),
            new UpdateClientRequest(tree.Org.NameAr, tree.Org.NameEn, "Active"));
        Assert.Equal(HttpStatusCode.OK, (await Api().SendAsync(Request(HttpMethod.Get, "/test/scoped", null, user, false, assignment.Id))).StatusCode);
    }

    [Fact]
    public async Task Created_client_is_invisible_from_another_clients_context()
    {
        var other = await AccessTestData.SeedAsync(factory, "IS1");
        var dto = await CreateAsync(NewClient("IS2"));

        await using var db = factory.CreateDbContext(TestCurrentContext.For(other.Org.Id, other.Root.Path));

        Assert.Equal([other.Org.Id], await db.Organizations.Select(o => o.Id).ToListAsync());
        Assert.Empty(await db.OrgUnits.Where(u => u.OrganizationId == dto.Id).ToListAsync());
        Assert.Empty(await db.Roles.Where(r => r.OrganizationId == dto.Id).ToListAsync());
        Assert.Empty(await db.LevelTypes.Where(l => l.OrganizationId == dto.Id).ToListAsync());
    }

    [Fact]
    public async Task Creating_a_client_is_audited()
    {
        var dto = await CreateAsync(NewClient("AU1"));

        await using var db = Admin();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(Organization) && e.EntityId == dto.Id));
    }
}