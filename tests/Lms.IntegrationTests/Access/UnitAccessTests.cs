using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class UnitAccessTests(LmsApiFactory factory)
{
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, RoleAssignment assignment, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Unit_inside_scope_is_allowed()
    {
        var tree = await AccessTestData.SeedAsync(factory, "UA1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Post, $"/test/units/{tree.School1.Id}/create-child", admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unit_in_sibling_subtree_returns_404_not_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "UA2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Post, $"/test/units/{tree.School2.Id}/create-child", admin);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unit_of_other_client_returns_404()
    {
        var a = await AccessTestData.SeedAsync(factory, "UA3");
        var b = await AccessTestData.SeedAsync(factory, "UA4");
        var admin = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Post, $"/test/units/{b.School1.Id}/create-child", admin);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_unit_returns_404()
    {
        var tree = await AccessTestData.SeedAsync(factory, "UA5");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Post, $"/test/units/{Guid.NewGuid()}/create-child", admin)).StatusCode);
    }

    [Fact]
    public async Task Visible_unit_without_the_permission_returns_403_MissingPermission()
    {
        var tree = await AccessTestData.SeedAsync(factory, "UA6");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Post, $"/test/units/{tree.School1.Id}/create-child", teacher);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingPermission", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Policy_on_target_unit_blocks_root_admin()
    {
        var tree = await AccessTestData.SeedAsync(factory, "UA7");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        factory.Policies.Block(Permissions.OrgUnits.Create, tree.School1);

        var response = await SendAsync(HttpMethod.Post, $"/test/units/{tree.School1.Id}/create-child", admin, "en");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BlockedByPolicy", body.GetProperty("reason").GetString());
        Assert.Equal(tree.School1.Id, body.GetProperty("blockingOrgUnitId").GetGuid());
        Assert.Equal(PolicyCatalog.BlockedMessageEn, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Client_root_check_denies_admin_assigned_below_root_with_OutOfScope()
    {
        var tree = await AccessTestData.SeedAsync(factory, "UA8");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Get, "/test/client-root/level-types", admin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OutOfScope", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Client_root_check_allows_root_admin()
    {
        var tree = await AccessTestData.SeedAsync(factory, "UA9");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/client-root/level-types", admin)).StatusCode);
    }

    [Fact]
    public async Task Client_root_check_denies_platform_admin_without_a_client()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/test/client-root/level-types");
        request.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.AdminHeader, "true");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingPermission", body.GetProperty("reason").GetString());
    }
}