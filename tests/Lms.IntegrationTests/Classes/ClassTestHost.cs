using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Classes;

/// <summary>Shared setup of the class endpoint tests: a client with an active year at the root and an admin.</summary>
internal sealed class ClassTestHost(LmsApiFactory factory, Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null) : IDisposable
{
    public OrgUnitTestHost Host { get; } = new(factory, configure);

    public void Dispose() => Host.Dispose();

    public sealed record World(AccessTree Tree, AcademicYear Year, RoleAssignment Admin);

    public async Task<World> SeedAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Set<AcademicYear>().SingleAsync(y => y.Id == year.Id);
            tracked.Activate();
            await db.SaveChangesAsync();
        }

        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return new World(tree, year, admin);
    }

    public async Task<AcademicYear> AddYearAsync(OrgUnit unit, bool closed = false)
    {
        var year = await AcademicTestKit.AddYearAsync(factory, unit);
        if (closed)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == year.Id)).Close();
            await db.SaveChangesAsync();
        }

        return year;
    }

    public Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        Host.SendAsync(method, url, caller, body);

    public static SaveClassRequest Save(
        OrgUnit unit, AcademicYear year, string? code = null, int? capacity = 30, Guid? homeroom = null) =>
        new(code ?? "c" + Guid.NewGuid().ToString("N")[..8], "فصل", "Class", unit.Id, year.Id, capacity, homeroom);

    public async Task<ClassSummaryDto> CreateAsync(RoleAssignment caller, SaveClassRequest request)
    {
        var response = await SendAsync(caller, HttpMethod.Post, Routes.Classes, request);
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ClassSummaryDto>())!;
    }

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
