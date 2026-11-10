using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.Access;

/// <summary>
/// Review Focus 2 end to end with the real <see cref="PolicyEvaluator"/> and real <see cref="OrgUnitPolicy"/> rows
/// (the other access tests use an in-memory evaluator).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RealPolicyEvaluationTests(LmsApiFactory factory)
{
    private WebApplicationFactory<Program> RealEvaluatorHost() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPolicyEvaluator>();
            services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        }));

    private async Task AddPolicyAsync(AccessTree tree, Lms.Domain.Organizations.OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitPolicy(tree.Org.Id, unit.Id, unit.Path, PolicyCatalog.CreateOrgUnits));
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> CreateOrgUnitAsync(
        HttpClient client, RoleAssignment assignment, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/test/org-units/create");
        request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task OrgAdmin_at_child_is_blocked_by_real_ancestor_policy()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RP1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AddPolicyAsync(tree, tree.Root);

        var response = await CreateOrgUnitAsync(RealEvaluatorHost().CreateClient(), admin, "en");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BlockedByPolicy", body.GetProperty("reason").GetString());
        Assert.Equal("Blocked by parent organization policy", body.GetProperty("detail").GetString());
        Assert.Equal(tree.Root.Id, body.GetProperty("blockingOrgUnitId").GetGuid());
    }

    [Fact]
    public async Task Arabic_request_gets_the_arabic_message_from_the_real_evaluator()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RP2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AddPolicyAsync(tree, tree.Root);

        var response = await CreateOrgUnitAsync(RealEvaluatorHost().CreateClient(), admin, "ar");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PolicyCatalog.BlockedMessageAr, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Sibling_policy_does_not_block_with_real_evaluator()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RP3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AddPolicyAsync(tree, tree.School1); // below the admin's unit, not an ancestor of it

        var response = await CreateOrgUnitAsync(RealEvaluatorHost().CreateClient(), admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Policy_of_another_client_does_not_block()
    {
        var a = await AccessTestData.SeedAsync(factory, "RP4");
        var b = await AccessTestData.SeedAsync(factory, "RP5");
        var admin = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AddPolicyAsync(b, b.Root);

        var response = await CreateOrgUnitAsync(RealEvaluatorHost().CreateClient(), admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Soft_deleted_policy_no_longer_blocks()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RP6");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AddPolicyAsync(tree, tree.Root);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(db.Set<OrgUnitPolicy>().Single(p => p.OrganizationId == tree.Org.Id));
            await db.SaveChangesAsync();
        }

        var response = await CreateOrgUnitAsync(RealEvaluatorHost().CreateClient(), admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}