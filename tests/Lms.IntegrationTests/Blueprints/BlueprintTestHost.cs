using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Blueprints;

/// <summary>A client with a course owned at School1, an OrgAdmin at the root and helpers for the blueprint endpoints.</summary>
internal sealed class BlueprintTestHost(LmsApiFactory factory, Action<IServiceCollection>? configure = null) : IDisposable
{
    public OrgUnitTestHost Host { get; } = new(factory, configure);

    public void Dispose() => Host.Dispose();

    public sealed record World(AccessTree Tree, RoleAssignment Admin, Course Course);

    public async Task<World> SeedAsync(string name, bool ownedAtRoot = false)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var course = await AcademicTestKit.AddCourseAsync(factory, ownedAtRoot ? tree.Root : tree.School1);
        return new World(tree, admin, course);
    }

    /// <summary>A Supervisor at School1 who supervises the course.</summary>
    public async Task<RoleAssignment> SupervisorAsync(World w, Course? course = null)
    {
        var user = await PeopleTestKit.AddUserAsync(factory);
        var assignment = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Supervisor, user.Id);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var target = course ?? w.Course;
        db.Add(new CourseSupervisor(await db.Set<Course>().SingleAsync(c => c.Id == target.Id), user.Id));
        await db.SaveChangesAsync();
        return assignment;
    }

    public async Task<RoleAssignment> RoleAsync(World w, string roleKey)
    {
        var user = await PeopleTestKit.AddUserAsync(factory);
        return await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, roleKey, user.Id);
    }

    public Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        Host.SendAsync(method, url, caller, body);

    public static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    public static BlueprintSectionInput Section(string title, params BlueprintLessonInput[] lessons) =>
        new(null, "قسم " + title, "Section " + title, lessons);

    public static BlueprintLessonInput Lesson(string title, int? offset = null) =>
        new(null, "درس " + title, "Lesson " + title, "أهداف", "Objectives", offset);

    public static SaveBlueprintDraftRequest Tree(int revision, params BlueprintSectionInput[] sections) => new(revision, sections);

    public async Task<BlueprintDto> CreateDraftAsync(RoleAssignment caller, Guid courseId)
    {
        var response = await SendAsync(caller, HttpMethod.Post, Routes.CourseBlueprintDraft(courseId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BlueprintDto>())!;
    }

    public async Task<BlueprintDto> SaveAsync(RoleAssignment caller, Guid courseId, SaveBlueprintDraftRequest request)
    {
        var response = await SendAsync(caller, HttpMethod.Put, Routes.CourseBlueprintDraft(courseId), request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BlueprintDto>())!;
    }

    public Task<HttpResponseMessage> PublishAsync(RoleAssignment caller, Guid courseId, string mode = BlueprintApplyModes.NewClassesOnly) =>
        SendAsync(caller, HttpMethod.Post, Routes.CourseBlueprintPublish(courseId), new PublishBlueprintRequest(mode));

    /// <summary>Creates a draft with one section and two lessons, saves it and publishes; returns the published tree.</summary>
    public async Task<BlueprintDto> PublishedAsync(RoleAssignment caller, Guid courseId)
    {
        var draft = await CreateDraftAsync(caller, courseId);
        await SaveAsync(caller, courseId, Tree(draft.Summary.Revision, Section("1", Lesson("a", 0), Lesson("b", 7))));
        var published = await PublishAsync(caller, courseId);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var version = (await published.Content.ReadFromJsonAsync<PublishBlueprintResultDto>())!.Blueprint.Version;
        var got = await SendAsync(caller, HttpMethod.Get, Routes.CourseBlueprintVersion(courseId, version));
        return (await got.Content.ReadFromJsonAsync<BlueprintDto>())!;
    }
}
