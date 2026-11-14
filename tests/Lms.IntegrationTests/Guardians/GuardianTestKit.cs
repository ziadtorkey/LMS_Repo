using Lms.Shared;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Guardians;

/// <summary>Seeded students and guardian requests for the guardian tests (student endpoints arrive in a later task).</summary>
internal sealed class GuardianTestKit(LmsApiFactory factory, WebApplicationFactory<Program> host) : IDisposable
{
    public void Dispose() => host.Dispose();

    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public LmsApiFactory Factory => factory;

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RoleAssignment caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return host.CreateClient().SendAsync(request);
    }

    public async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        return (tree, await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()));
    }

    public async Task<StudentProfile> StudentAsync(OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var student = new StudentProfile(
            unit, "S" + Guid.NewGuid().ToString("N")[..10], "طالب", "Student " + Guid.NewGuid().ToString("N")[..6],
            new DateOnly(2014, 5, 1), "Male", "Muslim", "EG");
        db.Set<StudentProfile>().Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    public async Task SetRequiredAsync(AccessTree tree, OrgUnit unit, bool required)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(tree.Org.Id, unit.Id, unit.Path, SettingKeys.GuardiansDataRequired.Name, required ? "true" : "false", false));
        await db.SaveChangesAsync();
    }

    public static string UniqueMobile() => "+2010" + Random.Shared.Next(10000000, 99999999);

    public static string UniqueNationalId() => "G" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    public static GuardianLinkInput NewGuardian(
        string? nationalId = null, string? mobile = null, string relation = "Father", bool primary = false, string? name = null) =>
        new(null, new GuardianDataInput("ولي أمر", name ?? "Guardian " + Guid.NewGuid().ToString("N")[..6], mobile ?? UniqueMobile(), null, nationalId), relation, primary);

    public async Task<IReadOnlyList<StudentGuardianDto>> LinksAsync(RoleAssignment who, Guid studentId) =>
        (await (await SendAsync(HttpMethod.Get, Routes.StudentGuardians(studentId), who))
            .Content.ReadFromJsonAsync<List<StudentGuardianDto>>(Web))!;

    public async Task<StudentGuardianDto> LinkOkAsync(RoleAssignment who, Guid studentId, GuardianLinkInput input)
    {
        var response = await SendAsync(HttpMethod.Post, Routes.StudentGuardians(studentId), who, input);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentGuardianDto>(Web))!;
    }

    public async Task<RoleAssignment> CallerWithRoleAsync(AccessTree tree, OrgUnit unit, string builtInRole) =>
        await AccessTestData.AssignAsync(factory, tree, unit, builtInRole, Guid.NewGuid());
}
