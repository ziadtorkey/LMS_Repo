using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class PeopleUsageCheckTests(LmsApiFactory factory)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Unit_with_live_students_cannot_be_deleted_unit_in_use()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PU1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await PeopleTestKit.AddStudentAsync(factory, tree.School2, PeopleTestKit.NewCode());
        using var host = new OrgUnitTestHost(factory);

        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(ProblemCodes.unit_in_use, body.GetProperty("code").GetString());
        Assert.Equal("students", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unit_that_is_the_home_of_staff_cannot_be_deleted_unit_in_use()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PU2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await PeopleTestKit.AddStaffAsync(factory, tree.School2);
        using var host = new OrgUnitTestHost(factory);

        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("staff", (await JsonAsync(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unit_without_people_can_still_be_deleted()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PU3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode()); // another unit
        using var host = new OrgUnitTestHost(factory);

        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
