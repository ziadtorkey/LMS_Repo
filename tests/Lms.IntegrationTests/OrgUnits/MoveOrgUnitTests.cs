using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.OrgUnits;

[Collection(ApiCollection.Name)]
public sealed class MoveOrgUnitTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    /// <summary>Root(L1) → School1(L1) → SectionA(L2) → Group(L3), and Root → School2(L1), with rows hanging off SectionA and Group.</summary>
    private sealed record World(
        AccessTree Tree, OrgUnit SectionA, OrgUnit Group, RoleAssignment RootAdmin, RoleAssignment GroupTeacher,
        RoleAssignment SectionTeacher);

    private async Task<World> BuildAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var l2 = new LevelType(tree.Org.Id, "٢", "L2", 2, true);
            var l3 = new LevelType(tree.Org.Id, "٣", "L3", 3, true);
            var sectionA = OrgUnit.CreateChild(tree.School1, l2.Id, "قسم أ", "Section A", "SEC-A");
            var group = OrgUnit.CreateChild(sectionA, l3.Id, "مجموعة", "Group", "GRP");
            db.AddRange(l2, l3, sectionA, group);
            await db.SaveChangesAsync();

            var rootAdmin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
            var teacherUser = Guid.NewGuid();
            var groupTeacher = await AccessTestData.AssignAsync(factory, tree, group, BuiltInRoles.Teacher, teacherUser);
            var sectionTeacher = await AccessTestData.AssignAsync(factory, tree, sectionA, BuiltInRoles.Teacher, Guid.NewGuid());
            db.Add(new OrgUnitSetting(tree.Org.Id, sectionA.Id, sectionA.Path, "Ui.DefaultCulture", "\"en\"", false));
            db.Add(new OrgUnitPolicy(tree.Org.Id, group.Id, group.Path, PolicyCatalog.CreateOrgUnits));
            await db.SaveChangesAsync();
            return new World(tree, sectionA, group, rootAdmin, groupTeacher, sectionTeacher);
        }
    }

    private Task<HttpResponseMessage> MoveAsync(RoleAssignment as_, OrgUnit unit, Guid newParentId) =>
        _host.SendAsync(HttpMethod.Put, Routes.OrgUnitMove(unit.Id), as_, new MoveOrgUnitRequest(newParentId));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Move_repaths_units_assignments_settings_policies_in_one_transaction()
    {
        var w = await BuildAsync("MV1");

        var response = await MoveAsync(w.RootAdmin, w.SectionA, w.Tree.Root.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newSectionPath = OrgUnitPath.Append(w.Tree.Root.Path, w.SectionA.Id);
        var newGroupPath = OrgUnitPath.Append(newSectionPath, w.Group.Id);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(newSectionPath, (await db.OrgUnits.SingleAsync(u => u.Id == w.SectionA.Id)).Path);
        Assert.Equal(newGroupPath, (await db.OrgUnits.SingleAsync(u => u.Id == w.Group.Id)).Path);
        Assert.Equal(w.Tree.Root.Id, (await db.OrgUnits.SingleAsync(u => u.Id == w.SectionA.Id)).ParentId);
        Assert.Equal(newGroupPath, (await db.RoleAssignments.SingleAsync(a => a.Id == w.GroupTeacher.Id)).OrgUnitPath);
        Assert.Equal(newSectionPath, (await db.RoleAssignments.SingleAsync(a => a.Id == w.SectionTeacher.Id)).OrgUnitPath);
        Assert.Equal(newSectionPath, (await db.Set<OrgUnitSetting>().SingleAsync(s => s.OrgUnitId == w.SectionA.Id)).OrgUnitPath);
        Assert.Equal(newGroupPath, (await db.Set<OrgUnitPolicy>().SingleAsync(p => p.OrgUnitId == w.Group.Id)).OrgUnitPath);
        // siblings and other units are untouched
        Assert.Equal(w.Tree.School1.Path, (await db.OrgUnits.SingleAsync(u => u.Id == w.Tree.School1.Id)).Path);
        Assert.Equal(w.Tree.School2.Path, (await db.OrgUnits.SingleAsync(u => u.Id == w.Tree.School2.Id)).Path);
    }

    [Fact]
    public async Task Depths_of_moved_subtree_are_updated()
    {
        var w = await BuildAsync("MV2");
        Assert.Equal((2, 3), (w.SectionA.Depth, w.Group.Depth));

        await MoveAsync(w.RootAdmin, w.SectionA, w.Tree.Root.Id);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, (await db.OrgUnits.SingleAsync(u => u.Id == w.SectionA.Id)).Depth);
        Assert.Equal(2, (await db.OrgUnits.SingleAsync(u => u.Id == w.Group.Id)).Depth);
    }

    [Fact]
    public async Task User_assigned_inside_moved_branch_keeps_exactly_the_new_subtree()
    {
        var w = await BuildAsync("MV3");
        await MoveAsync(w.RootAdmin, w.SectionA, w.Tree.School2.Id);
        var newGroupPath = OrgUnitPath.Append(OrgUnitPath.Append(w.Tree.School2.Path, w.SectionA.Id), w.Group.Id);

        // the teacher's context now scopes the group at its new place
        var scoped = await _host.SendAsync(HttpMethod.Get, "/test/scoped", w.GroupTeacher);
        Assert.Equal(newGroupPath, (await JsonAsync(scoped)).GetProperty("scopePath").GetString());

        // ...and it contains exactly the group: not School2, not the section, nothing under the old branch
        await using var inNewScope = factory.CreateDbContext(TestCurrentContext.For(w.Tree.Org.Id, newGroupPath));
        Assert.Equal([w.Group.Id], await inNewScope.OrgUnits.Select(u => u.Id).ToListAsync());
        await using var inOldScope = factory.CreateDbContext(TestCurrentContext.For(w.Tree.Org.Id, w.Group.Path));
        Assert.Empty(await inOldScope.OrgUnits.ToListAsync());
    }
    [Fact]
    public async Task Move_root_or_under_own_descendant_returns_409_invalid_move()
    {
        var w = await BuildAsync("MV4");

        var root = await MoveAsync(w.RootAdmin, w.Tree.Root, w.Tree.School1.Id);
        var self = await MoveAsync(w.RootAdmin, w.SectionA, w.SectionA.Id);
        var descendant = await MoveAsync(w.RootAdmin, w.SectionA, w.Group.Id);

        foreach (var response in new[] { root, self, descendant })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ProblemCodes.invalid_move, (await JsonAsync(response)).GetProperty("code").GetString());
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(w.SectionA.Path, (await db.OrgUnits.SingleAsync(u => u.Id == w.SectionA.Id)).Path);
    }

    [Fact]
    public async Task Move_under_a_unit_of_equal_or_deeper_level_returns_400_invalid_level_type()
    {
        var w = await BuildAsync("MV5");

        // School2 (level 1) under School1 (level 1): the parent must be strictly shallower
        var response = await MoveAsync(w.RootAdmin, w.Tree.School2, w.Tree.School1.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_level_type, (await JsonAsync(response)).GetProperty("code").GetString());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(w.Tree.School2.Path, (await db.OrgUnits.SingleAsync(u => u.Id == w.Tree.School2.Id)).Path);
    }
    [Fact]
    public async Task Move_needs_permission_at_source_and_target_404_for_other_subtree()
    {
        var w = await BuildAsync("MV6");
        var school1Admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        // target outside the admin's subtree → 404, nothing moved
        var outside = await MoveAsync(school1Admin, w.SectionA, w.Tree.School2.Id);
        // source outside the admin's subtree → 404
        var otherSource = await MoveAsync(school1Admin, w.Tree.School2, w.Tree.School1.Id);
        // no Move permission at all → 403
        var forbidden = await MoveAsync(teacher, w.SectionA, w.Tree.Root.Id);

        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherSource.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(w.SectionA.Path, (await db.OrgUnits.SingleAsync(u => u.Id == w.SectionA.Id)).Path);
    }

    [Fact]
    public async Task Move_inside_the_admins_own_subtree_is_allowed()
    {
        var w = await BuildAsync("MV7");
        var school1Admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        // Group (L3) under School1 (L1) directly: both inside the admin's subtree
        var response = await MoveAsync(school1Admin, w.Group, w.Tree.School1.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = (await response.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        Assert.Equal((w.Tree.School1.Id, 2), (node.ParentId, node.Depth));
    }

    [Fact]
    public async Task Move_to_the_current_parent_is_a_no_op_success()
    {
        var w = await BuildAsync("MV8");

        var response = await MoveAsync(w.RootAdmin, w.SectionA, w.Tree.School1.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(w.SectionA.Path, (await db.OrgUnits.SingleAsync(u => u.Id == w.SectionA.Id)).Path);
    }

    [Fact]
    public async Task Move_writes_audit_entry_with_old_and_new_parent()
    {
        var w = await BuildAsync("MV9");

        await MoveAsync(w.RootAdmin, w.SectionA, w.Tree.Root.Id);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = (await db.AuditEntries.Where(e => e.EntityId == w.SectionA.Id && e.Action == AuditAction.Updated)
            .OrderByDescending(e => e.TimestampUtc).ToListAsync()).First();
        var before = JsonDocument.Parse(entry.BeforeJson!).RootElement;
        var after = JsonDocument.Parse(entry.AfterJson!).RootElement;
        Assert.Equal(w.Tree.School1.Id, before.GetProperty("ParentId").GetGuid());
        Assert.Equal(w.Tree.Root.Id, after.GetProperty("ParentId").GetGuid());
        Assert.Equal(w.RootAdmin.UserId, entry.UserId);
    }

    [Fact]
    public async Task Failed_repath_rolls_the_whole_move_back()
    {
        var w = await BuildAsync("MV10");
        using var failing = new OrgUnitTestHost(factory, s =>
        {
            s.RemoveAll<IOrgUnitRepather>();
            s.AddScoped<IOrgUnitRepather, ThrowingRepather>();
        });

        var response = await failing.SendAsync(HttpMethod.Put, Routes.OrgUnitMove(w.SectionA.Id), w.RootAdmin,
            new MoveOrgUnitRequest(w.Tree.Root.Id));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var section = await db.OrgUnits.SingleAsync(u => u.Id == w.SectionA.Id);
        Assert.Equal((w.Tree.School1.Id, w.SectionA.Path), (section.ParentId, section.Path));
    }

    private sealed class ThrowingRepather : IOrgUnitRepather
    {
        public Task RepathAsync(Guid organizationId, string oldPrefix, string newPrefix, int depthDelta, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }
}