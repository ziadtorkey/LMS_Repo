using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.Academic;

[Collection(ApiCollection.Name)]
public sealed class AcademicUsageCheckTests(LmsApiFactory factory)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpResponseMessage Response, string? Detail)> DeleteSchool2Async(string tag, Func<AccessTree, Task> seed)
    {
        var tree = await AccessTestData.SeedAsync(factory, tag);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await seed(tree);
        using var host = new OrgUnitTestHost(factory);
        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin);
        return (response, response.StatusCode == HttpStatusCode.Conflict ? (await JsonAsync(response)).GetProperty("detail").GetString() : null);
    }

    [Fact]
    public async Task Unit_with_classes_cannot_be_deleted()
    {
        var (response, detail) = await DeleteSchool2Async("AU1", async tree =>
            await AcademicTestKit.AddClassAsync(factory, tree.School2, await AcademicTestKit.AddYearAsync(factory, tree.Root)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("classes", detail);
    }

    [Fact]
    public async Task Unit_that_owns_a_course_cannot_be_deleted()
    {
        var (response, detail) = await DeleteSchool2Async("AU2", tree => AcademicTestKit.AddCourseAsync(factory, tree.School2));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("courses", detail);
    }

    [Fact]
    public async Task Unit_with_an_allocated_course_cannot_be_deleted()
    {
        var (response, detail) = await DeleteSchool2Async("AU3", async tree =>
            await AcademicTestKit.AllocateAsync(factory, await AcademicTestKit.AddCourseAsync(factory, tree.Root), tree.School2));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("courses", detail);
    }

    [Fact]
    public async Task Unit_without_courses_or_classes_can_be_deleted()
    {
        var (response, _) = await DeleteSchool2Async("AU4", _ => Task.CompletedTask);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
