using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Me;

[Collection(ApiCollection.Name)]
public sealed class MeEndpointsTests(LmsApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<HttpResponseMessage> GetAsync(string url, Guid? user, Guid? assignment = null, string? name = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        }

        if (assignment is not null)
        {
            request.Headers.Add(ContextHeader.Name, assignment.ToString());
        }

        if (name is not null)
        {
            request.Headers.Add(TestAuthHandler.NameHeader, name);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Me_lists_only_valid_assignments_of_caller()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ME1");
        var user = Guid.NewGuid();
        var valid = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, user, validTo: Today.AddDays(-1));
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, user, validFrom: Today.AddDays(1));
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var deleted = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Student, user);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.RoleAssignments.SingleAsync(x => x.Id == deleted.Id));
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Users.Add(new Lms.Domain.Identity.User
            {
                Id = user, UserName = "tarek@lms.test", NormalizedUserName = "TAREK@LMS.TEST",
                DisplayNameAr = "المعلم طارق", DisplayNameEn = "Teacher Tarek",
            });
            await db.SaveChangesAsync();
        }

        var response = await GetAsync(Routes.Me, user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = (await response.Content.ReadFromJsonAsync<MeDto>())!;
        Assert.Equal(user, me.UserId);
        Assert.Equal("Teacher Tarek", me.DisplayNameEn);
        Assert.Equal("المعلم طارق", me.DisplayNameAr);
        Assert.False(me.IsPlatformAdmin);
        var context = Assert.Single(me.Contexts);
        Assert.Equal(valid.Id, context.AssignmentId);
        Assert.Equal(tree.Org.Id, context.OrganizationId);
        Assert.Equal(tree.School1.Id, context.OrgUnitId);
        Assert.Equal(tree.School1.NameEn, context.OrgUnitNameEn);
        Assert.Equal(tree.School1.NameAr, context.OrgUnitNameAr);
        Assert.Equal(tree.Org.NameEn, context.OrganizationNameEn);
        Assert.Equal("Teacher", context.RoleNameEn);
        Assert.Equal(tree.Roles[BuiltInRoles.Teacher].NameAr, context.RoleNameAr);
    }

    [Fact]
    public async Task Me_works_without_a_context_header_and_lists_contexts_across_clients()
    {
        var a = await AccessTestData.SeedAsync(factory, "ME2");
        var b = await AccessTestData.SeedAsync(factory, "ME3");
        var user = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.Supervisor, user);

        var me = (await (await GetAsync(Routes.Me, user)).Content.ReadFromJsonAsync<MeDto>())!;

        Assert.Equal(2, me.Contexts.Count);
        Assert.Equal(new[] { a.Org.Id, b.Org.Id }.Order(), me.Contexts.Select(c => c.OrganizationId).Order());
    }

    [Fact]
    public async Task Me_requires_authentication_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(Routes.Me, user: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(Routes.MyPermissions, user: null)).StatusCode);
    }

    [Fact]
    public async Task Permissions_reflect_role_and_exclude_policy_blocked()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ME4");
        var user = Guid.NewGuid();
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, user);
        factory.Policies.Block(Permissions.OrgUnits.Create, tree.Root);

        var response = await GetAsync(Routes.MyPermissions, user, a.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<EffectivePermissionsDto>())!;
        Assert.Equal(a.Id, dto.AssignmentId);
        Assert.DoesNotContain(Permissions.OrgUnits.Create, dto.Permissions);
        Assert.Equal(
            BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions
                .Where(p => p != Permissions.OrgUnits.Create).Order(),
            dto.Permissions.Order());
    }

    [Fact]
    public async Task Permissions_of_a_teacher_are_the_role_defaults()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ME5");
        var user = Guid.NewGuid();
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);

        var dto = (await (await GetAsync(Routes.MyPermissions, user, a.Id)).Content.ReadFromJsonAsync<EffectivePermissionsDto>())!;

        Assert.Equal(BuiltInRoles.Get(BuiltInRoles.Teacher).DefaultPermissions.Order(), dto.Permissions.Order());
    }

    [Fact]
    public async Task Permissions_without_a_context_header_is_403()
    {
        var response = await GetAsync(Routes.MyPermissions, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Permissions_with_other_users_assignment_is_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ME6");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(Routes.MyPermissions, Guid.NewGuid(), a.Id)).StatusCode);
    }

    [Fact]
    public async Task Me_omits_contexts_at_inactive_units()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ME7");
        var user = Guid.NewGuid();
        var atInactive = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        var atActive = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Teacher, user);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == tree.School1.Id);
            db.Entry(unit).Property(nameof(Lms.Domain.Organizations.OrgUnit.Status)).CurrentValue = Lms.Domain.Organizations.OrgUnitStatus.Inactive;
            await db.SaveChangesAsync();
        }

        var me = (await (await GetAsync(Routes.Me, user)).Content.ReadFromJsonAsync<MeDto>())!;

        Assert.Equal([atActive.Id], me.Contexts.Select(c => c.AssignmentId));
        Assert.DoesNotContain(me.Contexts, c => c.AssignmentId == atInactive.Id);
    }
}