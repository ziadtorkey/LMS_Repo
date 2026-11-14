using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Grading;

/// <summary>A client with an OrgAdmin at the root, a School1 admin and teacher, and helpers for the scale and weight-template endpoints.</summary>
internal sealed class GradingScaleTestHost(LmsApiFactory factory, Action<IServiceCollection>? configure = null) : IDisposable
{
    public OrgUnitTestHost Host { get; } = new(factory, configure);

    public void Dispose() => Host.Dispose();

    public sealed record World(AccessTree Tree, RoleAssignment RootAdmin, RoleAssignment SchoolAdmin, RoleAssignment Teacher);

    public async Task<World> SeedAsync(string name, bool presets = true)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        if (presets)
        {
            await ReconcileAsync(tree.Org.Id);
        }

        return new World(
            tree,
            await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id));
    }

    public async Task<RoleAssignment> AdminAtAsync(World w, Lms.Domain.Organizations.OrgUnit unit) =>
        await AccessTestData.AssignAsync(factory, w.Tree, unit, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id);

    public async Task<int> ReconcileAsync(Guid organizationId)
    {
        using var scope = Host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        return await scope.ServiceProvider.GetRequiredService<IPresetReconciler>().ReconcileAsync(organizationId, default);
    }

    public Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        Host.SendAsync(method, url, caller, body);

    public static IReadOnlyList<SaveGradingScaleBandRequest> TwoBands(decimal? passPoints = null) =>
    [
        new(0, 0, 60, "F", "راسب", "Fail", null, null, passPoints is null ? null : 0m),
        new(1, 60, 100, "P", "ناجح", "Pass", null, null, passPoints),
    ];

    public static SaveGradingScaleRequest Scale(string name = "S", string kind = ScaleKinds.Letter, IReadOnlyList<SaveGradingScaleBandRequest>? bands = null) =>
        new("سلم " + name, "Scale " + name, kind, bands ?? TwoBands());

    public async Task<GradingScaleDto> CreateScaleAsync(RoleAssignment caller, Guid unitId, SaveGradingScaleRequest? request = null)
    {
        var response = await SendAsync(caller, HttpMethod.Post, Lms.Shared.Routes.OrgUnitGradingScales(unitId), request ?? Scale());
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GradingScaleDto>())!;
    }

    public async Task<IReadOnlyList<GradingScaleDto>> ListAsync(RoleAssignment caller, Guid unitId)
    {
        var response = await SendAsync(caller, HttpMethod.Get, Lms.Shared.Routes.OrgUnitGradingScales(unitId));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<List<GradingScaleDto>>())!;
    }

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    public async Task<List<GradingScale>> StoredAsync(Guid unitId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<GradingScale>().Where(s => s.OrgUnitId == unitId).OrderBy(s => s.NameEn).ToListAsync();
    }
}
