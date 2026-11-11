using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Review Focus 1 in the probe suite, on a host with the **real** <see cref="PolicyEvaluator"/> and real policy rows: a
/// unit-targeted action is checked at the **target** unit, so a policy on an ancestor of the target blocks even the client
/// root admin (403 BlockedByPolicy, exact en/ar copy, blockingOrgUnitId), while a policy beside the target does not.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M1PolicyBlockProbeTests(LmsApiFactory factory)
{
    private WebApplicationFactory<Program> RealEvaluatorHost() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPolicyEvaluator>();
            services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        }));

    [Theory]
    [InlineData(Caller.RootAdmin)]
    [InlineData(Caller.School1Admin)]
    public async Task Policy_on_an_ancestor_of_the_target_blocks_creating_a_unit_with_the_exact_message(Caller caller)
    {
        var world = await M1ProbeWorld.GetAsync(factory);
        var (blocked, policyUnit) = await BlockedBranchAsync(world);
        using var host = RealEvaluatorHost();

        using var en = await CreateChildAsync(host, world, caller, blocked, "en");
        using var ar = await CreateChildAsync(host, world, caller, blocked, "ar");

        Assert.Equal(403, (int)en.StatusCode);
        var body = await en.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BlockedByPolicy", body.GetProperty("reason").GetString());
        Assert.Equal(PolicyCatalog.BlockedMessageEn, body.GetProperty("detail").GetString());
        Assert.Equal(policyUnit.Id, body.GetProperty("blockingOrgUnitId").GetGuid());
        Assert.Equal(403, (int)ar.StatusCode);
        Assert.Equal(PolicyCatalog.BlockedMessageAr, (await ar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Policy_beside_the_target_does_not_block()
    {
        var world = await M1ProbeWorld.GetAsync(factory);
        await BlockedBranchAsync(world);
        var sibling = await world.NewUnitAsync(world.Callers.A.School1);   // beside the policy unit
        var otherSchool = await world.NewUnitAsync(world.Callers.A.School2);
        using var host = RealEvaluatorHost();

        using var underSibling = await CreateChildAsync(host, world, Caller.RootAdmin, sibling, "en");
        using var underSchool2 = await CreateChildAsync(host, world, Caller.RootAdmin, otherSchool, "en");

        Assert.Equal(201, (int)underSibling.StatusCode);
        Assert.Equal(201, (int)underSchool2.StatusCode);
    }

    /// <summary>A fresh unit P below School1 holding the CreateOrgUnits policy, and a child of P (the creation target).</summary>
    private static async Task<(OrgUnit Target, OrgUnit PolicyUnit)> BlockedBranchAsync(M1ProbeWorld world)
    {
        var policyUnit = await world.NewUnitAsync(world.Callers.A.School1);
        await using (var db = world.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitPolicy(world.Callers.A.Org.Id, policyUnit.Id, policyUnit.Path, PolicyCatalog.CreateOrgUnits));
            await db.SaveChangesAsync();
        }

        return (await world.NewUnitAsync(policyUnit), policyUnit);
    }

    private static async Task<HttpResponseMessage> CreateChildAsync(
        WebApplicationFactory<Program> host, M1ProbeWorld world, Caller caller, OrgUnit parent, string language)
    {
        var body = new CreateOrgUnitRequest(parent.Id, await DeeperLevelIdAsync(world, parent), "وحدة", "Policy probe", "PB" + world.Next());
        var request = new HttpRequestMessage(HttpMethod.Post, Routes.OrgUnits) { Content = JsonContent.Create(body) };
        var assignment = world.Callers.AssignmentOf(caller);
        request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        request.Headers.Add("Accept-Language", language);
        return await host.CreateClient().SendAsync(request);
    }

    /// <summary>A client A level type deeper than the parent's, so the child passes the depth rule.</summary>
    private static async Task<Guid> DeeperLevelIdAsync(M1ProbeWorld world, OrgUnit parent)
    {
        await using var db = world.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var parentLevel = await db.LevelTypes.SingleAsync(l => l.Id == parent.LevelTypeId);
        var level = new LevelType(world.Callers.A.Org.Id, "مستوى", "Policy probe level", parentLevel.Order + 1 + world.Next() % 1000, true);
        db.Add(level);
        await db.SaveChangesAsync();
        return level.Id;
    }
}
