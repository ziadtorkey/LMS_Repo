using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Lms.Domain.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class PermissionFilterTests(LmsApiFactory factory)
{
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, RoleAssignment? assignment, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (assignment is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        }

        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Allowed_request_reaches_the_endpoint()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PF1");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/org-units/view", a)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/test/org-units/view", null)).StatusCode);
    }

    [Fact]
    public async Task Missing_permission_gets_403_with_reason()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PF2");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Get, "/test/org-units/view", a);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingPermission", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Group_level_RequirePermission_protects_its_endpoints()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PF3");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/test/audit/", teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/audit/", manager)).StatusCode);
    }

    [Fact]
    public async Task OrgAdmin_at_child_is_blocked_by_ancestor_policy_with_english_message()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PF4");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        factory.Policies.Block(Permissions.OrgUnits.Create, tree.Root);

        var response = await SendAsync(HttpMethod.Post, "/test/org-units/create", a, "en");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BlockedByPolicy", body.GetProperty("reason").GetString());
        Assert.Equal("Blocked by parent organization policy", body.GetProperty("detail").GetString());
        Assert.Equal(tree.Root.Id, body.GetProperty("blockingOrgUnitId").GetGuid());
    }

    [Fact]
    public async Task Arabic_request_gets_arabic_block_message()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PF5");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        factory.Policies.Block(Permissions.OrgUnits.Create, tree.Root);

        var response = await SendAsync(HttpMethod.Post, "/test/org-units/create", a, "ar-EG,ar;q=0.9,en;q=0.5");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PolicyCatalog.BlockedMessageAr, body.GetProperty("detail").GetString());
    }

    [Fact]
    public void RequirePermission_rejects_unknown_permission_names_at_startup()
    {
        var app = WebApplication.CreateBuilder().Build();

        Assert.Throws<ArgumentException>(() =>
        {
            Lms.Api.Infrastructure.EndpointAuthorizationExtensions.RequirePermission(app.MapGet("/x", () => 1), "Nope.Nothing");
        });
    }
}