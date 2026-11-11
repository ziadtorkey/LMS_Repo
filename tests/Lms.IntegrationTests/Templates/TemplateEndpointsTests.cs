using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Templates;

[Collection(ApiCollection.Name)]
public sealed class TemplateEndpointsTests(LmsApiFactory factory) : IDisposable
{
    private static readonly Dictionary<string, string> Year = new() { ["2026"] = "{Year}" };

    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    /// <summary>Root → School1 → Grade (level 2, "Section 1 2026") → ClassA (level 3); School2 is a sibling target.</summary>
    internal sealed record Fixture(AccessTree Tree, RoleAssignment Admin, OrgUnit Grade, OrgUnit ClassA, LevelType Level2);

    internal static async Task<Fixture> SetUpAsync(LmsApiFactory factory, string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var level2 = new LevelType(tree.Org.Id, "ثانٍ", "Second", 2, true);
        var level3 = new LevelType(tree.Org.Id, "ثالث", "Third", 3, true);
        var grade = OrgUnit.CreateChild(tree.School1, level2.Id, "الصف 2026", "Section 1 2026", "G-2026");
        var classA = OrgUnit.CreateChild(grade, level3.Id, "فصل أ", "Class A 2026", "C-2026");
        db.AddRange(level2, level3, grade, classA);
        await db.SaveChangesAsync();
        return new Fixture(tree, admin, grade, classA, level2);
    }

    private Task<HttpResponseMessage> SaveAsync(
        RoleAssignment who, OrgUnit source, string visibility = "Private", Dictionary<string, string>? map = null) =>
        _host.SendAsync(HttpMethod.Post, Routes.Templates, who, new SaveTemplateRequest(
            source.Id, "قالب", "Template", visibility, false, false, map ?? Year));

    private static async Task<OrgTemplateDetailDto> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<OrgTemplateDetailDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    private Task<HttpResponseMessage> ApplyAsync(
        RoleAssignment who, Guid templateId, OrgUnit target, Dictionary<string, string>? values) =>
        _host.SendAsync(HttpMethod.Post, Routes.TemplateApply(templateId), who, new ApplyTemplateRequest(target.Id, values ?? []));

    [Fact]
    public async Task Save_branch_with_year_placeholder_and_apply_as_2027()
    {
        var f = await SetUpAsync(factory, "TP1");

        var saved = await SaveAsync(f.Admin, f.Grade);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var detail = await DetailAsync(saved);
        Assert.Equal(["Year"], detail.Summary.Placeholders);
        Assert.Equal(2, detail.Summary.UnitCount);
        Assert.Equal("Section 1 {Year}", detail.Nodes[0].NameEn);
        Assert.Equal("G-{Year}", detail.Nodes[0].Code);
        Assert.Equal("الصف {Year}", detail.Nodes[0].NameAr);

        var applied = await ApplyAsync(f.Admin, detail.Summary.Id, f.Tree.School2, new() { ["Year"] = "2027" });

        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        var result = (await applied.Content.ReadFromJsonAsync<ApplyTemplateResultDto>())!;
        Assert.Equal(2, result.UnitsCreated);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var root = await db.OrgUnits.SingleAsync(u => u.Id == result.NewRootId);
        Assert.Equal("Section 1 2027", root.NameEn);
        Assert.Equal("الصف 2027", root.NameAr);
        Assert.Equal("G-2027", root.Code);
        Assert.Equal(f.Tree.School2.Id, root.ParentId);
        Assert.True(await db.OrgUnits.AnyAsync(u => u.ParentId == root.Id && u.Code == "C-2027" && u.NameEn == "Class A 2027"));
    }

    [Fact]
    public async Task Missing_placeholder_is_400_and_extra_is_400()
    {
        var f = await SetUpAsync(factory, "TP2");
        var id = (await DetailAsync(await SaveAsync(f.Admin, f.Grade))).Summary.Id;

        var missing = await ApplyAsync(f.Admin, id, f.Tree.School2, []);
        var extra = await ApplyAsync(f.Admin, id, f.Tree.School2, new() { ["Year"] = "2027", ["Term"] = "1" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("placeholder_missing", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        Assert.Contains("placeholder_unknown", await extra.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Value_that_makes_an_invalid_code_is_400_and_taken_code_is_409()
    {
        var f = await SetUpAsync(factory, "TP3");
        var id = (await DetailAsync(await SaveAsync(f.Admin, f.Grade))).Summary.Id;

        var invalid = await ApplyAsync(f.Admin, id, f.Tree.School2, new() { ["Year"] = "20 27" });
        var taken = await ApplyAsync(f.Admin, id, f.Tree.School2, new() { ["Year"] = "2026" }); // G-2026 already exists

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("code_taken", await taken.Content.ReadAsStringAsync());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.OrgUnits.AnyAsync(u => u.ParentId == f.Tree.School2.Id)); // neither failed apply created anything
    }

    [Fact]
    public async Task Private_template_of_School1_is_invisible_to_School2_admin_404()
    {
        var f = await SetUpAsync(factory, "TP4");
        var school2Admin = await AccessTestData.AssignAsync(factory, f.Tree, f.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school1Admin = await AccessTestData.AssignAsync(factory, f.Tree, f.Tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var id = (await DetailAsync(await SaveAsync(f.Admin, f.Grade, "Private"))).Summary.Id;

        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.Template(id), school2Admin)).StatusCode);
        var list = (await (await _host.SendAsync(HttpMethod.Get, Routes.Templates, school2Admin)).Content.ReadFromJsonAsync<List<OrgTemplateSummaryDto>>())!;
        Assert.DoesNotContain(list, t => t.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, (await ApplyAsync(school2Admin, id, f.Tree.School2, new() { ["Year"] = "2027" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Delete, Routes.Template(id), school2Admin)).StatusCode);

        // The owner's own subtree and the root admin still see it.
        Assert.Equal(HttpStatusCode.OK, (await _host.SendAsync(HttpMethod.Get, Routes.Template(id), school1Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _host.SendAsync(HttpMethod.Get, Routes.Template(id), f.Admin)).StatusCode);
    }

    [Fact]
    public async Task Shared_template_is_visible_client_wide()
    {
        var f = await SetUpAsync(factory, "TP5");
        var school2Admin = await AccessTestData.AssignAsync(factory, f.Tree, f.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var id = (await DetailAsync(await SaveAsync(f.Admin, f.Grade, "Shared"))).Summary.Id;

        var detail = await _host.SendAsync(HttpMethod.Get, Routes.Template(id), school2Admin);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var summary = (await DetailAsync(detail)).Summary;
        Assert.Equal(f.Grade.Id, summary.OwnerOrgUnitId);
        Assert.Equal(f.Grade.NameEn, summary.OwnerNameEn); // owner outside School2's scope, name still resolved

        // Applying under their own unit works; deleting someone else's template does not (owner unit not visible).
        var applied = await ApplyAsync(school2Admin, id, f.Tree.School2, new() { ["Year"] = "2031" });
        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Delete, Routes.Template(id), school2Admin)).StatusCode);
    }

    [Fact]
    public async Task Apply_under_policy_blocked_unit_is_403()
    {
        var f = await SetUpAsync(factory, "TP6");
        var id = (await DetailAsync(await SaveAsync(f.Admin, f.Grade))).Summary.Id;
        await _host.AddPolicyAsync(f.Tree, f.Tree.School2, PolicyCatalog.CreateOrgUnits);

        var response = await ApplyAsync(f.Admin, id, f.Tree.School2, new() { ["Year"] = "2027" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Apply_with_a_missing_level_type_is_400()
    {
        var f = await SetUpAsync(factory, "TP7");
        var id = (await DetailAsync(await SaveAsync(f.Admin, f.Grade))).Summary.Id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // Template's root level is Second; make it impossible by applying below a unit of the same depth.
            var target = OrgUnit.CreateChild(f.Tree.School2, f.Level2.Id, "ب", "Deep", "DEEP");
            db.Add(target);
            await db.SaveChangesAsync();
            var response = await ApplyAsync(f.Admin, id, target, new() { ["Year"] = "2028" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("invalid_level_type", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Invalid_save_requests_are_400()
    {
        var f = await SetUpAsync(factory, "TP8");

        var badVisibility = await SaveAsync(f.Admin, f.Grade, "Public");
        var badToken = await SaveAsync(f.Admin, f.Grade, "Private", new() { ["2026"] = "Year" });
        var noName = await _host.SendAsync(HttpMethod.Post, Routes.Templates, f.Admin,
            new SaveTemplateRequest(f.Grade.Id, "", "T", "Private", false, false, new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, badVisibility.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
    }

    [Fact]
    public async Task Delete_is_soft_and_audited()
    {
        var f = await SetUpAsync(factory, "TP9");
        var id = (await DetailAsync(await SaveAsync(f.Admin, f.Grade))).Summary.Id;

        var deleted = await _host.SendAsync(HttpMethod.Delete, Routes.Template(id), f.Admin);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.Template(id), f.Admin)).StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.Set<OrgTemplate>().IgnoreQueryFilters().AnyAsync(t => t.Id == id && t.IsDeleted));
        var actions = await db.AuditEntries.Where(e => e.EntityId == id).Select(e => e.Action.ToString()).ToListAsync();
        Assert.Contains("Created", actions);
        Assert.Contains("Deleted", actions);
    }

    [Fact]
    public async Task Teacher_without_template_permissions_gets_403()
    {
        var f = await SetUpAsync(factory, "TP10");
        var teacher = await AccessTestData.AssignAsync(factory, f.Tree, f.Tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await _host.SendAsync(HttpMethod.Get, Routes.Templates, teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(teacher, f.Grade)).StatusCode);
    }
}
