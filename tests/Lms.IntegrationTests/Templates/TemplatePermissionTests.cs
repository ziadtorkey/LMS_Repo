using System.Net;
using System.Net.Http.Json;
using Lms.Api.Features.Branches;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Templates;

/// <summary>
/// Applying a template must not be a way around the permissions of what it carries: policies and settings are written
/// at the target only when the caller could also set them there by hand (the same rule as copy branch).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TemplatePermissionTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<RoleAssignment> CallerWithAsync(AccessTree tree, params string[] permissions)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var role = new Role(tree.Org.Id, null, "دور", "Applier " + Guid.NewGuid().ToString("N")[..6], RoleBaseKind.Staff);
        db.Add(role);
        db.AddRange(permissions.Select(permission => new RolePermission(tree.Org.Id, role.Id, permission)));
        var assignment = new RoleAssignment(tree.Root, Guid.NewGuid(), role.Id);
        db.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private async Task<(int Policies, int Settings)> RowsOfAsync(Guid orgId, string year)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var ids = await db.OrgUnits.Where(u => u.OrganizationId == orgId && u.Code.EndsWith(year)).Select(u => u.Id).ToListAsync();
        return (await db.Set<OrgUnitPolicy>().CountAsync(p => ids.Contains(p.OrgUnitId)),
            await db.Set<OrgUnitSetting>().CountAsync(p => ids.Contains(p.OrgUnitId)));
    }

    [Fact]
    public async Task Policies_and_settings_are_applied_only_with_their_update_permissions()
    {
        var f = await TemplateEndpointsTests.SetUpAsync(factory, "TPM1");
        await _host.AddPolicyAsync(f.Tree, f.Grade, PolicyCatalog.CreateOrgUnits);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitSetting(f.Tree.Org.Id, f.ClassA.Id, f.ClassA.Path, SettingKeys.FilesMaxUploadMb.Name, "50", false));
            await db.SaveChangesAsync();
        }

        var saved = await _host.SendAsync(HttpMethod.Post, Routes.Templates, f.Admin, new SaveTemplateRequest(
            f.Grade.Id, "قالب", "Template", "Shared", true, true, new Dictionary<string, string> { ["2026"] = "{Year}" }));
        saved.EnsureSuccessStatusCode();
        var id = (await saved.Content.ReadFromJsonAsync<OrgTemplateDetailDto>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!.Summary.Id;
        string[] apply = [Permissions.Templates.Apply, Permissions.Templates.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.View];

        async Task<ApplyTemplateResultDto> ApplyAsync(RoleAssignment who, string year)
        {
            var response = await _host.SendAsync(HttpMethod.Post, Routes.TemplateApply(id), who,
                new ApplyTemplateRequest(f.Tree.School2.Id, new Dictionary<string, string> { ["Year"] = year }));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ApplyTemplateResultDto>())!;
        }

        // Neither update permission: the structure is applied, policies and settings are skipped and reported.
        var neither = await ApplyAsync(await CallerWithAsync(f.Tree, apply), "3001");
        Assert.Equal(2, neither.UnitsCreated);
        Assert.Equal([CopyBranchEndpoint.PoliciesSkipped, CopyBranchEndpoint.SettingsSkipped], neither.SkippedLockedSettings);
        Assert.Equal((0, 0), await RowsOfAsync(f.Tree.Org.Id, "3001"));

        // Policies.Update only
        var policiesOnly = await ApplyAsync(await CallerWithAsync(f.Tree, [.. apply, Permissions.Policies.Update]), "3002");
        Assert.Equal([CopyBranchEndpoint.SettingsSkipped], policiesOnly.SkippedLockedSettings);
        Assert.Equal((1, 0), await RowsOfAsync(f.Tree.Org.Id, "3002"));

        // Settings.Update only
        var settingsOnly = await ApplyAsync(await CallerWithAsync(f.Tree, [.. apply, Permissions.Settings.Update]), "3003");
        Assert.Equal([CopyBranchEndpoint.PoliciesSkipped], settingsOnly.SkippedLockedSettings);
        Assert.Equal((0, 1), await RowsOfAsync(f.Tree.Org.Id, "3003"));

        // Both: everything is applied, nothing skipped.
        var both = await ApplyAsync(await CallerWithAsync(f.Tree, [.. apply, Permissions.Policies.Update, Permissions.Settings.Update]), "3004");
        Assert.Empty(both.SkippedLockedSettings);
        Assert.Equal((1, 1), await RowsOfAsync(f.Tree.Org.Id, "3004"));
    }

    [Fact]
    public async Task A_template_without_policies_or_settings_reports_nothing_skipped()
    {
        var f = await TemplateEndpointsTests.SetUpAsync(factory, "TPM2");
        var saved = await _host.SendAsync(HttpMethod.Post, Routes.Templates, f.Admin, new SaveTemplateRequest(
            f.Grade.Id, "قالب", "Plain", "Shared", false, false, new Dictionary<string, string> { ["2026"] = "{Year}" }));
        var id = (await saved.Content.ReadFromJsonAsync<OrgTemplateDetailDto>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!.Summary.Id;
        var caller = await CallerWithAsync(f.Tree, Permissions.Templates.Apply, Permissions.Templates.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.View);

        var response = await _host.SendAsync(HttpMethod.Post, Routes.TemplateApply(id), caller,
            new ApplyTemplateRequest(f.Tree.School2.Id, new Dictionary<string, string> { ["Year"] = "3005" }));

        Assert.Empty((await response.Content.ReadFromJsonAsync<ApplyTemplateResultDto>())!.SkippedLockedSettings);
    }
}
