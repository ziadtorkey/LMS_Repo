using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Audit;

[Collection(ApiCollection.Name)]
public sealed class AuditViewerTests(LmsApiFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal static HttpRequestMessage Request(string url, RoleAssignment? assignment, bool platformAdmin = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.UserHeader, (assignment?.UserId ?? Guid.NewGuid()).ToString());
        if (assignment is not null)
        {
            request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        }

        if (platformAdmin)
        {
            request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        }

        return request;
    }

    internal static async Task<AuditEntry> AddEntryAsync(
        LmsApiFactory factory, Guid? orgId, string? path, string entityType = "Probe", AuditAction action = AuditAction.Updated,
        Guid? userId = null, DateTime? at = null, string? before = null, string? after = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = new AuditEntry(
            orgId, userId, null, action, entityType, Guid.NewGuid(), before, after, at ?? DateTime.UtcNow, "10.0.0.1", path);
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    private async Task<PagedResult<AuditEntryDto>> QueryAsync(string query, RoleAssignment? a, bool admin = false)
    {
        var response = await factory.CreateClient().SendAsync(Request("/api/audit" + query, a, admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<AuditEntryDto>>(Web))!;
    }

    [Fact]
    public async Task Viewer_filters_by_user_entity_action_and_date()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var user = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddDays(-10);
        var hit = await AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Role", AuditAction.Created, user, t0);
        await AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Role", AuditAction.Created, Guid.NewGuid(), t0);
        await AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Term", AuditAction.Created, user, t0);
        await AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Role", AuditAction.Deleted, user, t0);
        await AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Role", AuditAction.Created, user, t0.AddDays(-30));

        var from = Uri.EscapeDataString(t0.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(t0.AddDays(1).ToString("O"));
        var page = await QueryAsync(
            $"?userId={user}&entityType=Role&action=Created&fromUtc={from}&toUtc={to}", admin);

        var row = Assert.Single(page.Items);
        Assert.Equal(hit.Id, row.Id);
        Assert.Equal("Role", row.EntityType);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Results_are_newest_first_and_paged()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var user = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, userId: user, at: DateTime.UtcNow.AddMinutes(-i));
        }

        var page = await QueryAsync($"?userId={user}&page=2&pageSize=2", admin);

        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(3, page.Total);
        Assert.Single(page.Items);
        var all = await QueryAsync($"?userId={user}&pageSize=200", admin);
        Assert.Equal(all.Items.OrderByDescending(x => x.TimestampUtc).Select(x => x.Id), all.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task School_manager_sees_only_entries_of_own_subtree()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV3");
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, Guid.NewGuid());
        var own = await AddEntryAsync(factory, tree.Org.Id, tree.School1.Path);
        var deeper = await AddEntryAsync(factory, tree.Org.Id, tree.School1.Path + Guid.NewGuid().ToString("N") + "/");
        await AddEntryAsync(factory, tree.Org.Id, tree.School2.Path);
        await AddEntryAsync(factory, tree.Org.Id, tree.Root.Path);
        await AddEntryAsync(factory, tree.Org.Id, null);

        var page = await QueryAsync("?pageSize=200&entityType=Probe", manager);

        Assert.Equal(new[] { own.Id, deeper.Id }.Order(), page.Items.Select(x => x.Id).Order());
    }

    [Fact]
    public async Task Root_viewer_sees_entries_without_a_unit_but_school_viewer_does_not()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV4");
        var root = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var noUnit = await AddEntryAsync(factory, tree.Org.Id, null);

        Assert.Contains((await QueryAsync("?pageSize=200", root)).Items, x => x.Id == noUnit.Id);
        Assert.DoesNotContain((await QueryAsync("?pageSize=200", school)).Items, x => x.Id == noUnit.Id);
    }

    [Fact]
    public async Task Client_A_never_sees_client_B_entries()
    {
        var a = await AccessTestData.SeedAsync(factory, "AV5A");
        var b = await AccessTestData.SeedAsync(factory, "AV5B");
        var adminA = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var entryB = await AddEntryAsync(factory, b.Org.Id, b.Root.Path);

        // Even asking for B's organization explicitly: the filter is honoured for the platform admin only.
        var page = await QueryAsync($"?organizationId={b.Org.Id}&pageSize=200", adminA);
        Assert.DoesNotContain(page.Items, x => x.Id == entryB.Id);

        var detail = await factory.CreateClient().SendAsync(Request($"/api/audit/{entryB.Id}", adminA));
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_sees_all_and_may_filter_by_organization()
    {
        var a = await AccessTestData.SeedAsync(factory, "AV6A");
        var b = await AccessTestData.SeedAsync(factory, "AV6B");
        var ea = await AddEntryAsync(factory, a.Org.Id, a.Root.Path, "Probe");
        var eb = await AddEntryAsync(factory, b.Org.Id, b.Root.Path, "Probe");
        var platform = await AddEntryAsync(factory, null, null, "LoginFailed", AuditAction.SecurityEvent);

        var all = await QueryAsync("?pageSize=200&entityType=Probe", null, admin: true);
        Assert.Contains(all.Items, x => x.Id == ea.Id);
        Assert.Contains(all.Items, x => x.Id == eb.Id);
        var events = await QueryAsync("?pageSize=200&action=SecurityEvent", null, admin: true);
        Assert.Contains(events.Items, x => x.Id == platform.Id);

        var onlyB = await QueryAsync($"?organizationId={b.Org.Id}&entityType=Probe", null, admin: true);
        Assert.Equal(new[] { eb.Id }, onlyB.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Detail_returns_before_and_after_json_with_arabic_intact()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV7");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var entry = await AddEntryAsync(
            factory, tree.Org.Id, tree.Root.Path, before: "{\"NameAr\":\"مدرسة النور\"}", after: "{\"NameAr\":\"مدرسة الأمل 🌟\"}");

        var response = await factory.CreateClient().SendAsync(Request($"/api/audit/{entry.Id}", admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<AuditEntryDetailDto>(Web))!;
        Assert.Equal(entry.Id, detail.Entry.Id);
        Assert.Contains("مدرسة النور", detail.BeforeJson);
        Assert.Contains("مدرسة الأمل 🌟", detail.AfterJson);
    }

    [Fact]
    public async Task Row_shows_actor_role_and_unit_names()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV8");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var actor = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, Guid.NewGuid());

        // Written by the real interceptor: a change made while acting under the manager assignment.
        var ctx = new TestCurrentContext
        {
            UserId = actor.UserId, OrganizationId = tree.Org.Id, AssignmentId = actor.Id, ScopePath = tree.School1.Path,
        };
        await using (var db = factory.CreateDbContext(ctx))
        {
            var unit = await db.OrgUnits.SingleAsync(x => x.Id == tree.School1.Id);
            db.Entry(unit).Property(x => x.NameEn).CurrentValue = "Renamed AV8";
            await db.SaveChangesAsync();
        }

        var page = await QueryAsync($"?userId={actor.UserId}", admin);

        var row = Assert.Single(page.Items);
        Assert.Equal(tree.Roles[BuiltInRoles.Manager].NameEn, row.RoleNameEn);
        Assert.Equal(tree.School1.NameAr, row.OrgUnitNameAr);
        Assert.Equal("OrgUnit", row.EntityType);
    }

    [Fact]
    public async Task Unknown_action_filter_is_400()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV11");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().SendAsync(Request("/api/audit?action=Bogus", admin))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().SendAsync(Request("/api/audit/export?format=csv&action=Bogus", admin))).StatusCode);
    }

    [Fact]
    public async Task Another_clients_role_and_unit_names_are_not_resolved()
    {
        var a = await AccessTestData.SeedAsync(factory, "AV12A");
        var b = await AccessTestData.SeedAsync(factory, "AV12B");
        var adminA = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var foreign = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.Manager, Guid.NewGuid());

        // A client-A entry that (corrupt or forged) points at client B's assignment and unit.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.AuditEntries.Add(new AuditEntry(
                a.Org.Id, Guid.NewGuid(), foreign.Id, AuditAction.Updated, "Probe", Guid.NewGuid(), null, null,
                DateTime.UtcNow, null, a.Root.Path + b.Root.Id.ToString("N") + "/"));
            await db.SaveChangesAsync();
        }

        var page = await QueryAsync("?entityType=Probe&pageSize=200", adminA);

        var row = Assert.Single(page.Items);
        Assert.Null(row.RoleNameEn);
        Assert.Null(row.OrgUnitNameEn);
    }

    [Fact]
    public async Task Interceptor_stamps_the_unit_path()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV9");
        var ctx = TestCurrentContext.For(tree.Org.Id, tree.Root.Path);
        Guid assignmentId;
        await using (var db = factory.CreateDbContext(ctx))
        {
            var assignment = new RoleAssignment(tree.School2, Guid.NewGuid(), tree.Roles[BuiltInRoles.Teacher].Id);
            db.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        await using var db2 = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db2.AuditEntries.SingleAsync(x => x.EntityId == assignmentId);
        Assert.Equal(tree.School2.Path, entry.OrgUnitPath);
        var created = await db2.AuditEntries.SingleAsync(x => x.EntityId == tree.School1.Id);
        Assert.Equal(tree.School1.Path, created.OrgUnitPath);
    }

    [Fact]
    public async Task Teacher_without_Audit_View_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AV10");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().SendAsync(Request("/api/audit", teacher))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().SendAsync(Request($"/api/audit/{Guid.NewGuid()}", teacher))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().SendAsync(Request("/api/audit/export?format=csv", teacher))).StatusCode);
    }
}
