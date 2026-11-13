using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Configuration;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.Packages;

/// <summary>
/// D57, backend that needs no attempts: the grading method is chosen at attach (default Highest), changed through
/// <c>PUT /api/packages/{id}/grading-method</c> and read back on <c>PackageUploadDto</c>. The permission is <c>Modules.Manage</c> on the
/// ClassCourse (404 for other staff, 403 for a student), the closed-class guard applies, and a change calls the recompute hook.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PackageGradingMethodTests(LmsApiFactory factory) : IDisposable
{
    private const string ContentOrigin = "http://127.0.0.1:5999";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly List<ContentTestHost> _hosts = [];
    private readonly List<OrgUnitTestHost> _apis = [];

    public void Dispose()
    {
        _hosts.ForEach(h => h.Dispose());
        _apis.ForEach(h => h.Dispose());
    }

    private sealed class CountingRecompute : IPackageGradeRecompute
    {
        public static readonly List<Guid> Calls = [];

        public Task RecomputeAsync(Guid packageId, CancellationToken ct = default)
        {
            lock (Calls)
            {
                Calls.Add(packageId);
            }

            return Task.CompletedTask;
        }
    }

    private sealed record Setup(ContentWorld World, OrgUnitTestHost Api);

    private async Task<Setup> SetUpAsync(string name)
    {
        var host = new ContentTestHost(factory);
        _hosts.Add(host);
        var world = await host.SetUpAsync(name);
        var api = new OrgUnitTestHost(factory, services =>
        {
            services.Configure<PackageHostOptions>(o => o.Origin = ContentOrigin);
            services.RemoveAll<IPackageGradeRecompute>();
            services.AddScoped<IPackageGradeRecompute, CountingRecompute>();
        });
        _apis.Add(api);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(world.Tree.Org.Id, world.Tree.Root.Id, world.Tree.Root.Path, FeatureFlags.ScormContent.Name, "true", false));
        await db.SaveChangesAsync();
        return new Setup(world, api);
    }

    private async Task<Guid> ReadyUploadAsync(Setup s)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var file = new Lms.Domain.Files.StoredFile(
            s.World.ClassCourse.OrganizationId, s.World.ClassCourse.OrgUnitId, s.World.ClassCourse.OrgUnitPath, "package.zip", "application/zip", 8, DateTime.UtcNow);
        var upload = new PackageUpload(s.World.ClassCourse, ContentTestHost.Lesson1, PackageKinds.Scorm, "course.zip", file.Id, 8, new string('c', 64));
        upload.MarkReady("Course", ScormVersions.Scorm12, null, "p/");
        db.AddRange(file, upload);
        await db.SaveChangesAsync();
        return upload.Id;
    }

    private static ModuleSettingsInput Module() =>
        new("حزمة", "Package", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null);

    private async Task<PackageUploadDto> GetAsync(Setup s, Guid id) =>
        (await (await s.Api.SendAsync(HttpMethod.Get, Routes.Package(id), s.World.Teacher)).Content.ReadFromJsonAsync<PackageUploadDto>(Web))!;

    private async Task<string> StoredMethodAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<PackageUpload>().SingleAsync(p => p.Id == id)).GradingMethod;
    }

    private static Task<HttpResponseMessage> Put(Setup s, Guid id, Lms.Domain.Access.RoleAssignment who, object? body) =>
        s.Api.SendAsync(HttpMethod.Put, Routes.PackageGradingMethod(id), who, body);

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Attach_defaults_to_Highest_and_the_read_shows_it_only_once_attached()
    {
        var s = await SetUpAsync("GM1");
        var id = await ReadyUploadAsync(s);
        Assert.Null((await GetAsync(s, id)).GradingMethod);

        var attached = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(id), s.World.Teacher, new AttachPackageModuleRequest(Module()));

        Assert.Equal(HttpStatusCode.Created, attached.StatusCode);
        Assert.Equal(PackageGradingMethods.Highest, (await GetAsync(s, id)).GradingMethod);
        Assert.Equal(PackageGradingMethods.Highest, await StoredMethodAsync(id));
    }

    [Theory]
    [InlineData(PackageGradingMethods.Last)]
    [InlineData(PackageGradingMethods.First)]
    public async Task Attach_stores_the_chosen_method(string method)
    {
        var s = await SetUpAsync("GM2" + method);
        var id = await ReadyUploadAsync(s);

        await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(id), s.World.Teacher, new AttachPackageModuleRequest(Module(), method));

        Assert.Equal(method, (await GetAsync(s, id)).GradingMethod);
    }

    [Theory]
    [InlineData("Average")]
    [InlineData("highest")]
    [InlineData("")]
    public async Task Attach_with_an_unknown_method_is_400_and_attaches_nothing(string method)
    {
        var s = await SetUpAsync("GM3" + method);
        var id = await ReadyUploadAsync(s);

        var response = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(id), s.World.Teacher, new AttachPackageModuleRequest(Module(), method));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await CodeAsync(response));
        Assert.Null((await GetAsync(s, id)).ModuleId);
    }

    [Fact]
    public async Task Changing_the_method_before_attach_is_409_package_not_attached()
    {
        var s = await SetUpAsync("GM4");
        var id = await ReadyUploadAsync(s);

        var response = await Put(s, id, s.World.Teacher, new UpdatePackageGradingMethodRequest(PackageGradingMethods.Last));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("package_not_attached", await CodeAsync(response));
    }

    [Fact]
    public async Task Changing_the_method_updates_it_returns_the_dto_and_calls_the_recompute_hook_only_on_a_change()
    {
        var s = await SetUpAsync("GM5");
        var id = await ReadyUploadAsync(s);
        await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(id), s.World.Teacher, new AttachPackageModuleRequest(Module()));
        lock (CountingRecompute.Calls)
        {
            CountingRecompute.Calls.Clear();
        }

        var changed = await Put(s, id, s.World.Teacher, new UpdatePackageGradingMethodRequest(PackageGradingMethods.First));
        var same = await Put(s, id, s.World.Teacher, new UpdatePackageGradingMethodRequest(PackageGradingMethods.First));

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(PackageGradingMethods.First, (await changed.Content.ReadFromJsonAsync<PackageUploadDto>(Web))!.GradingMethod);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(PackageGradingMethods.First, await StoredMethodAsync(id));
        lock (CountingRecompute.Calls)
        {
            Assert.Equal([id], CountingRecompute.Calls.Where(c => c == id));
        }
    }

    [Theory]
    [InlineData("Average")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_unknown_or_missing_method_is_400_validation_failed(string? method)
    {
        var s = await SetUpAsync("GM6" + method);
        var id = await ReadyUploadAsync(s);
        await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(id), s.World.Teacher, new AttachPackageModuleRequest(Module()));

        var response = await Put(s, id, s.World.Teacher, method is null ? new { } : new UpdatePackageGradingMethodRequest(method));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await CodeAsync(response));
        Assert.Equal(PackageGradingMethods.Highest, await StoredMethodAsync(id));
    }

    [Fact]
    public async Task A_student_is_403_and_staff_of_another_class_course_or_an_unknown_package_are_404()
    {
        var s = await SetUpAsync("GM7");
        var id = await ReadyUploadAsync(s);
        await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(id), s.World.Teacher, new AttachPackageModuleRequest(Module()));
        var body = new UpdatePackageGradingMethodRequest(PackageGradingMethods.Last);

        Assert.Equal(HttpStatusCode.Forbidden, (await Put(s, id, s.World.Student, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(s, id, s.World.OtherTeacher, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(s, Guid.NewGuid(), s.World.Teacher, body)).StatusCode);
        Assert.Equal(PackageGradingMethods.Highest, await StoredMethodAsync(id));
    }

    [Fact]
    public async Task Another_client_cannot_see_or_change_the_method()
    {
        var a = await SetUpAsync("GM8A");
        var b = await SetUpAsync("GM8B");
        var id = await ReadyUploadAsync(a);
        await a.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(id), a.World.Teacher, new AttachPackageModuleRequest(Module()));

        var response = await Put(b, id, b.World.Teacher, new UpdatePackageGradingMethodRequest(PackageGradingMethods.Last));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(PackageGradingMethods.Highest, await StoredMethodAsync(id));
    }
}
