using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.LevelTypes;

[Collection(ApiCollection.Name)]
public sealed class LevelTypeEndpointsTests(LmsApiFactory factory)
{
    private HttpClient Client => factory.CreateClient();

    private static HttpRequestMessage Request(HttpMethod method, string url, RoleAssignment? who, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (who is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, who.Id.ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static SaveLevelTypeRequest Save(string en, string? ar = null, bool canHost = false) =>
        new(ar ?? "ar-" + en, en, canHost);

    private async Task<IReadOnlyList<LevelTypeDto>> ListAsync(RoleAssignment who)
    {
        var response = await Client.SendAsync(Request(HttpMethod.Get, Routes.LevelTypes, who));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<LevelTypeDto>>())!;
    }

    private async Task<(AccessTree Tree, RoleAssignment Admin)> RootAdminAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Root_admin_creates_updates_reorders_and_deletes_level_types()
    {
        var (_, admin) = await RootAdminAsync("LT1");
        var seeded = Assert.Single(await ListAsync(admin)); // the "Level" type the tree was created with
        Assert.Equal(1, seeded.Order);

        var stageResponse = await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Stage")));
        Assert.Equal(HttpStatusCode.Created, stageResponse.StatusCode);
        var stage = (await stageResponse.Content.ReadFromJsonAsync<LevelTypeDto>())!;
        Assert.Equal(2, stage.Order);
        Assert.Equal("ar-Stage", stage.NameAr);
        Assert.Equal(0, stage.UnitCount);
        Assert.EndsWith(Routes.LevelType(stage.Id), stageResponse.Headers.Location!.OriginalString);

        var section = (await (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Section", canHost: true))))
            .Content.ReadFromJsonAsync<LevelTypeDto>())!;
        Assert.Equal(3, section.Order);
        Assert.True(section.CanHostClasses);

        var updated = await Client.SendAsync(Request(HttpMethod.Put, Routes.LevelType(stage.Id), admin, Save("Stage 2", "مرحلة", canHost: true)));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedDto = (await updated.Content.ReadFromJsonAsync<LevelTypeDto>())!;
        Assert.Equal("Stage 2", updatedDto.NameEn);
        Assert.Equal("مرحلة", updatedDto.NameAr);
        Assert.True(updatedDto.CanHostClasses);
        Assert.Equal(2, updatedDto.Order);

        var reorder = await Client.SendAsync(Request(HttpMethod.Put, Routes.LevelTypesOrder, admin,
            new ReorderLevelTypesRequest([section.Id, seeded.Id, stage.Id])));
        Assert.Equal(HttpStatusCode.NoContent, reorder.StatusCode);
        var afterReorder = await ListAsync(admin);
        Assert.Equal([section.Id, seeded.Id, stage.Id], afterReorder.Select(x => x.Id));
        Assert.Equal([1, 2, 3], afterReorder.Select(x => x.Order));

        var deleted = await Client.SendAsync(Request(HttpMethod.Delete, Routes.LevelType(section.Id), admin));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await ListAsync(admin);
        Assert.Equal([seeded.Id, stage.Id], remaining.Select(x => x.Id));
        Assert.Equal([1, 2], remaining.Select(x => x.Order)); // order stays contiguous

        // The next new type goes to the end.
        var next = (await (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Year"))))
            .Content.ReadFromJsonAsync<LevelTypeDto>())!;
        Assert.Equal(3, next.Order);
    }

    [Fact]
    public async Task School_admin_cannot_edit_level_types_OutOfScope()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LT2");
        var rootAdmin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var existing = Assert.Single(await ListAsync(rootAdmin));

        // Reading is fine from anywhere in the client...
        Assert.Single(await ListAsync(schoolAdmin));

        // ...writing is only for the client root.
        var attempts = new[]
        {
            Request(HttpMethod.Post, Routes.LevelTypes, schoolAdmin, Save("Nope")),
            Request(HttpMethod.Put, Routes.LevelType(existing.Id), schoolAdmin, Save("Nope")),
            Request(HttpMethod.Put, Routes.LevelTypesOrder, schoolAdmin, new ReorderLevelTypesRequest([existing.Id])),
            Request(HttpMethod.Delete, Routes.LevelType(existing.Id), schoolAdmin),
        };
        foreach (var attempt in attempts)
        {
            var response = await Client.SendAsync(attempt);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("OutOfScope", (await ProblemAsync(response)).GetProperty("reason").GetString());
        }

        Assert.Equal(existing.NameEn, Assert.Single(await ListAsync(rootAdmin)).NameEn);
    }

    [Fact]
    public async Task Manager_can_view_but_not_create_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LT3");
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.Single(await ListAsync(manager));

        var response = await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, manager, Save("Nope")));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("MissingPermission", (await ProblemAsync(response)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Teacher_without_the_view_permission_cannot_list_and_anonymous_gets_401()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LT4");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await Client.SendAsync(Request(HttpMethod.Get, Routes.LevelTypes, teacher))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.SendAsync(Request(HttpMethod.Get, Routes.LevelTypes, null))).StatusCode);
    }

    [Fact]
    public async Task Delete_used_level_type_returns_409_level_type_in_use()
    {
        var (_, admin) = await RootAdminAsync("LT5");
        var used = Assert.Single(await ListAsync(admin));
        Assert.Equal(3, used.UnitCount); // Root, School1 and School2 use it

        var response = await Client.SendAsync(Request(HttpMethod.Delete, Routes.LevelType(used.Id), admin));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.level_type_in_use, (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Single(await ListAsync(admin));
    }

    [Fact]
    public async Task Usage_count_covers_the_whole_client_even_for_a_school_scope()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LT6");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(3, Assert.Single(await ListAsync(schoolAdmin)).UnitCount); // all three units, not just the School1 subtree
    }

    [Fact]
    public async Task Reorder_with_missing_id_returns_400()
    {
        var (_, admin) = await RootAdminAsync("LT7");
        var first = Assert.Single(await ListAsync(admin));
        var second = (await (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Stage"))))
            .Content.ReadFromJsonAsync<LevelTypeDto>())!;

        var bodies = new[]
        {
            new ReorderLevelTypesRequest([first.Id]),                       // missing one
            new ReorderLevelTypesRequest([first.Id, second.Id, Guid.NewGuid()]), // unknown extra
            new ReorderLevelTypesRequest([first.Id, first.Id]),             // duplicate instead of the other
            new ReorderLevelTypesRequest([]),
        };
        foreach (var body in bodies)
        {
            var response = await Client.SendAsync(Request(HttpMethod.Put, Routes.LevelTypesOrder, admin, body));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.validation_failed, (await ProblemAsync(response)).GetProperty("code").GetString());
        }

        Assert.Equal([first.Id, second.Id], (await ListAsync(admin)).Select(x => x.Id));
    }

    [Fact]
    public async Task Client_B_level_types_are_invisible_and_unknown_id_is_404()
    {
        var (_, adminA) = await RootAdminAsync("LT8A");
        var (_, adminB) = await RootAdminAsync("LT8B");
        var typeB = Assert.Single(await ListAsync(adminB));

        var listA = await ListAsync(adminA);
        Assert.DoesNotContain(listA, x => x.Id == typeB.Id);

        foreach (var request in new[]
                 {
                     Request(HttpMethod.Put, Routes.LevelType(typeB.Id), adminA, Save("Hijack")),
                     Request(HttpMethod.Delete, Routes.LevelType(typeB.Id), adminA),
                     Request(HttpMethod.Put, Routes.LevelType(Guid.NewGuid()), adminA, Save("Ghost")),
                     Request(HttpMethod.Delete, Routes.LevelType(Guid.NewGuid()), adminA),
                 })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Client.SendAsync(request)).StatusCode);
        }

        // Reordering with another client's id is refused and changes nothing there.
        var reorder = await Client.SendAsync(Request(HttpMethod.Put, Routes.LevelTypesOrder, adminA,
            new ReorderLevelTypesRequest([.. listA.Select(x => x.Id), typeB.Id])));
        Assert.Equal(HttpStatusCode.BadRequest, reorder.StatusCode);
        Assert.Equal(typeB.NameEn, Assert.Single(await ListAsync(adminB)).NameEn);
    }

    [Fact]
    public async Task Platform_admin_without_a_client_context_cannot_list_level_types_of_all_clients()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Routes.LevelTypes);
        request.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.AdminHeader, "true");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_names_are_409_name_taken_per_language_but_free_again_after_delete()
    {
        var (_, admin) = await RootAdminAsync("LT9");
        var stage = (await (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Stage", "مرحلة"))))
            .Content.ReadFromJsonAsync<LevelTypeDto>())!;

        var sameEn = await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("stage ", "اسم آخر")));
        var sameAr = await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Other", "مرحلة")));
        Assert.Equal(HttpStatusCode.Conflict, sameEn.StatusCode);
        Assert.Equal(ProblemCodes.name_taken, (await ProblemAsync(sameEn)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, sameAr.StatusCode);

        // Updating an item to its own names is fine; to another item's names is not.
        Assert.Equal(HttpStatusCode.OK,
            (await Client.SendAsync(Request(HttpMethod.Put, Routes.LevelType(stage.Id), admin, Save("Stage", "مرحلة", canHost: true)))).StatusCode);
        var other = (await (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Other", "آخر"))))
            .Content.ReadFromJsonAsync<LevelTypeDto>())!;
        Assert.Equal(HttpStatusCode.Conflict,
            (await Client.SendAsync(Request(HttpMethod.Put, Routes.LevelType(other.Id), admin, Save("STAGE", "آخر")))).StatusCode);

        // The same names in another client are no conflict.
        var (_, adminB) = await RootAdminAsync("LT9B");
        Assert.Equal(HttpStatusCode.Created,
            (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, adminB, Save("Stage", "مرحلة")))).StatusCode);

        // After a delete the names can be reused.
        Assert.Equal(HttpStatusCode.NoContent, (await Client.SendAsync(Request(HttpMethod.Delete, Routes.LevelType(stage.Id), admin))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Stage", "مرحلة")))).StatusCode);
    }

    [Fact]
    public async Task Invalid_names_return_validation_problem()
    {
        var (_, admin) = await RootAdminAsync("LT10");
        var tooLong = new string('x', 201);

        foreach (var body in new[] { new SaveLevelTypeRequest("  ", "Stage", false), new SaveLevelTypeRequest("مرحلة", "", false),
                     new SaveLevelTypeRequest("مرحلة", tooLong, false), new SaveLevelTypeRequest(tooLong, "Stage", false) })
        {
            var response = await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, body));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ProblemAsync(response);
            Assert.Equal(ProblemCodes.validation_failed, problem.GetProperty("code").GetString());
            Assert.True(problem.TryGetProperty("errors", out _));
        }

        Assert.Single(await ListAsync(admin)); // nothing was created
    }

    [Fact]
    public async Task Names_are_trimmed_and_arabic_round_trips()
    {
        var (_, admin) = await RootAdminAsync("LT11");

        var created = (await (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin,
            new SaveLevelTypeRequest("  كلية الحاسبات 🌟 ", " Faculty ", true)))).Content.ReadFromJsonAsync<LevelTypeDto>())!;

        Assert.Equal("كلية الحاسبات 🌟", created.NameAr);
        Assert.Equal("Faculty", created.NameEn);
    }

    [Fact]
    public async Task Level_type_change_is_audited()
    {
        var (tree, admin) = await RootAdminAsync("LT12");
        var created = (await (await Client.SendAsync(Request(HttpMethod.Post, Routes.LevelTypes, admin, Save("Stage"))))
            .Content.ReadFromJsonAsync<LevelTypeDto>())!;
        await Client.SendAsync(Request(HttpMethod.Put, Routes.LevelType(created.Id), admin, Save("Stage renamed")));
        await Client.SendAsync(Request(HttpMethod.Delete, Routes.LevelType(created.Id), admin));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.Where(x => x.EntityId == created.Id).OrderBy(x => x.TimestampUtc).ToListAsync();

        Assert.Equal([AuditAction.Created, AuditAction.Updated, AuditAction.Deleted], entries.Select(x => x.Action));
        Assert.All(entries, e => Assert.Equal(tree.Org.Id, e.OrganizationId));
        Assert.All(entries, e => Assert.Equal(admin.UserId, e.UserId));
        Assert.Contains("Stage renamed", entries[1].AfterJson);
    }
}
