using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Settings;

/// <summary>The enable hook: a registered <see cref="ISettingChangeGuard"/> can refuse a setting change with a 409 before anything is stored.</summary>
[Collection(ApiCollection.Name)]
public sealed class SettingChangeGuardTests(LmsApiFactory factory) : IDisposable
{
    private sealed class RefuseTrue(string setting, string code) : ISettingChangeGuard
    {
        public List<(Guid UnitId, string Value)> Calls { get; } = [];

        public string SettingName => setting;

        public Task<string?> RefuseAsync(OrgUnit unit, string valueJson, CancellationToken ct)
        {
            Calls.Add((unit.Id, valueJson));
            return Task.FromResult<string?>(valueJson == "true" ? code : null);
        }
    }

    private readonly List<OrgUnitTestHost> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private OrgUnitTestHost HostWith(params ISettingChangeGuard[] guards)
    {
        var host = new OrgUnitTestHost(factory, services =>
        {
            foreach (var guard in guards)
            {
                services.AddSingleton(guard);
            }
        });
        _hosts.Add(host);
        return host;
    }

    private async Task<(AccessTree Tree, Lms.Domain.Access.RoleAssignment Admin)> AdminAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin);
    }

    private static async Task<bool> EffectiveAsync(OrgUnitTestHost host, Lms.Domain.Access.RoleAssignment admin, Guid unitId, string name)
    {
        var list = await (await host.SendAsync(HttpMethod.Get, Routes.OrgUnitSettings(unitId), admin)).Content.ReadFromJsonAsync<List<EffectiveSettingDto>>();
        return JsonDocument.Parse(list!.Single(x => x.Name == name).ValueJson).RootElement.GetBoolean();
    }

    [Fact]
    public async Task A_refusing_guard_answers_409_with_its_code_and_nothing_is_stored()
    {
        var guard = new RefuseTrue(FeatureFlags.VirtualClasses.Name, ProblemCodes.provider_not_configured);
        var host = HostWith(guard);
        var (tree, admin) = await AdminAsync("SG1");

        var response = await host.SendAsync(HttpMethod.Put, Routes.OrgUnitSetting(tree.Root.Id, FeatureFlags.VirtualClasses.Name), admin,
            new SetSettingRequest("true", false));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.provider_not_configured, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(await EffectiveAsync(host, admin, tree.Root.Id, FeatureFlags.VirtualClasses.Name));
        Assert.Equal([(tree.Root.Id, "true")], guard.Calls);
    }

    [Fact]
    public async Task The_guard_lets_a_change_it_does_not_refuse_through()
    {
        var host = HostWith(new RefuseTrue(FeatureFlags.VirtualClasses.Name, ProblemCodes.provider_not_configured));
        var (tree, admin) = await AdminAsync("SG2");

        var response = await host.SendAsync(HttpMethod.Put, Routes.OrgUnitSetting(tree.Root.Id, FeatureFlags.VirtualClasses.Name), admin,
            new SetSettingRequest("false", false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_guard_is_asked_only_about_its_own_setting()
    {
        var guard = new RefuseTrue(FeatureFlags.VirtualClasses.Name, ProblemCodes.provider_not_configured);
        var host = HostWith(guard);
        var (tree, admin) = await AdminAsync("SG3");

        var response = await host.SendAsync(HttpMethod.Put, Routes.OrgUnitSetting(tree.Root.Id, FeatureFlags.ScormContent.Name), admin,
            new SetSettingRequest("true", false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(guard.Calls);
    }

    [Fact]
    public async Task An_invalid_value_is_refused_by_validation_before_any_guard_runs()
    {
        var guard = new RefuseTrue(FeatureFlags.VirtualClasses.Name, ProblemCodes.provider_not_configured);
        var host = HostWith(guard);
        var (tree, admin) = await AdminAsync("SG4");

        var response = await host.SendAsync(HttpMethod.Put, Routes.OrgUnitSetting(tree.Root.Id, FeatureFlags.VirtualClasses.Name), admin,
            new SetSettingRequest("\"yes\"", false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(guard.Calls);
    }

    [Fact]
    public async Task The_first_refusal_wins_when_two_guards_watch_one_setting()
    {
        var first = new RefuseTrue(FeatureFlags.VirtualClasses.Name, ProblemCodes.provider_not_configured);
        var second = new RefuseTrue(FeatureFlags.VirtualClasses.Name, ProblemCodes.package_host_not_configured);
        var host = HostWith(first, second);
        var (tree, admin) = await AdminAsync("SG5");

        var response = await host.SendAsync(HttpMethod.Put, Routes.OrgUnitSetting(tree.Root.Id, FeatureFlags.VirtualClasses.Name), admin,
            new SetSettingRequest("true", false));

        Assert.Equal(ProblemCodes.provider_not_configured, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty(second.Calls);
    }
}
