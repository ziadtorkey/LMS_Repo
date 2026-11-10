using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class CurrentContextTests(LmsApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<HttpResponseMessage> GetAsync(Guid? user, string? header, bool admin = false, params string[] extraHeaderValues)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/test/scoped");
        if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        }

        if (admin)
        {
            request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        }

        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(ContextHeader.Name, [header, .. extraHeaderValues]);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task AssertInvalidContextAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_context", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Request_without_context_header_gets_403_on_scoped_endpoint()
    {
        var response = await GetAsync(Guid.NewGuid(), header: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Header_with_other_users_assignment_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC1");
        var owner = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, owner);

        await AssertInvalidContextAsync(await GetAsync(Guid.NewGuid(), assignment.Id.ToString()));
    }

    [Fact]
    public async Task Expired_assignment_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC2");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(
            factory, tree, tree.School1, BuiltInRoles.Teacher, user, validTo: Today.AddDays(-1));

        await AssertInvalidContextAsync(await GetAsync(user, assignment.Id.ToString()));
    }

    [Fact]
    public async Task Not_yet_valid_assignment_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC3");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(
            factory, tree, tree.School1, BuiltInRoles.Teacher, user, validFrom: Today.AddDays(1));

        await AssertInvalidContextAsync(await GetAsync(user, assignment.Id.ToString()));
    }

    [Fact]
    public async Task Soft_deleted_assignment_role_or_unit_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC4");
        var user = Guid.NewGuid();
        var deletedAssignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        var deletedRole = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, user);
        var deletedUnit = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, user);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.RoleAssignments.SingleAsync(x => x.Id == deletedAssignment.Id));
            db.Remove(await db.Roles.SingleAsync(x => x.Id == deletedRole.RoleId));
            db.Remove(await db.OrgUnits.SingleAsync(x => x.Id == deletedUnit.OrgUnitId));
            await db.SaveChangesAsync();
        }

        await AssertInvalidContextAsync(await GetAsync(user, deletedAssignment.Id.ToString()));
        await AssertInvalidContextAsync(await GetAsync(user, deletedRole.Id.ToString()));
        await AssertInvalidContextAsync(await GetAsync(user, deletedUnit.Id.ToString()));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Malformed_header_gets_403_not_500(string header)
    {
        await AssertInvalidContextAsync(await GetAsync(Guid.NewGuid(), header));
    }

    [Fact]
    public async Task Multiple_header_values_get_403()
    {
        await AssertInvalidContextAsync(
            await GetAsync(Guid.NewGuid(), Guid.NewGuid().ToString(), extraHeaderValues: Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task Header_without_authentication_is_ignored_and_context_stays_empty()
    {
        var response = await GetAsync(user: null, header: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_header_sets_scope_to_assignment_unit_path()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC5");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(
            factory, tree, tree.School1, BuiltInRoles.Teacher, user, Today.AddDays(-5), Today);

        var response = await GetAsync(user, assignment.Id.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tree.School1.Path, body.GetProperty("scopePath").GetString());
        Assert.Equal(tree.Org.Id, body.GetProperty("organizationId").GetGuid());
        Assert.Equal(assignment.Id, body.GetProperty("assignmentId").GetGuid());
        Assert.Equal(user, body.GetProperty("userId").GetGuid());
        Assert.False(body.GetProperty("isPlatformAdmin").GetBoolean());
    }

    [Fact]
    public async Task Platform_admin_without_header_is_platform_admin()
    {
        var response = await GetAsync(Guid.NewGuid(), header: null, admin: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isPlatformAdmin").GetBoolean());
    }

    [Fact]
    public async Task Unknown_api_route_returns_404()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task DeactivateAsync(Lms.Domain.Organizations.OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.OrgUnits.SingleAsync(u => u.Id == unit.Id);
        db.Entry(row).Property(nameof(Lms.Domain.Organizations.OrgUnit.Status)).CurrentValue = Lms.Domain.Organizations.OrgUnitStatus.Inactive;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Assignment_at_inactive_unit_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC6");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        await DeactivateAsync(tree.School1);

        await AssertInvalidContextAsync(await GetAsync(user, assignment.Id.ToString()));
    }

    [Fact]
    public async Task Assignment_below_inactive_unit_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC7");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        await DeactivateAsync(tree.Root);

        await AssertInvalidContextAsync(await GetAsync(user, assignment.Id.ToString()));
    }

    [Fact]
    public async Task Admin_assigned_above_an_inactive_unit_keeps_access()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC8");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, user);
        await DeactivateAsync(tree.School1);

        var response = await GetAsync(user, assignment.Id.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Inactive_sibling_does_not_affect_an_assignment_at_the_other_sibling()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC9");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        await DeactivateAsync(tree.School2);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(user, assignment.Id.ToString())).StatusCode);
    }
}