using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Content;
using Lms.Domain.Files;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Files;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lms.IntegrationTests.Library;

/// <summary>
/// A client with a root, two schools, an admin at the root, a school admin per school, a Teacher and a Student at School1 (the
/// student's home is School1) and a second level type for the level filter.
/// </summary>
internal sealed class LibraryTestWorld : IDisposable
{
    public static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 library sample bytes");

    private readonly LmsApiFactory _factory;
    private readonly OrgUnitTestHost _host;

    private LibraryTestWorld(LmsApiFactory factory, AccessTree tree)
    {
        _factory = factory;
        _host = new OrgUnitTestHost(factory);
        Tree = tree;
    }

    public AccessTree Tree { get; }
    public RoleAssignment Admin { get; private set; } = null!;
    public RoleAssignment School1Admin { get; private set; } = null!;
    public RoleAssignment School2Admin { get; private set; } = null!;
    public RoleAssignment Teacher { get; private set; } = null!;
    public RoleAssignment Student { get; private set; } = null!;
    public Guid LevelOfSchool1 => Tree.School1.LevelTypeId;
    public Guid OtherLevel { get; private set; }

    public static async Task<LibraryTestWorld> CreateAsync(LmsApiFactory factory, string name)
    {
        var world = new LibraryTestWorld(factory, await AccessTestData.SeedAsync(factory, name));
        await world.SeedAsync();
        return world;
    }

    public void Dispose() => _host.Dispose();

    public Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(method, url, caller, body);

    public async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    public static SaveLibraryResourceRequest Link(
        OrgUnit owner, string titleEn = "Khan", string? url = "https://example.org/a", string[]? tags = null, Guid[]? levels = null,
        Guid? courseId = null, string type = LibraryResourceTypes.Link, Guid? fileAssetId = null, bool downloadAllowed = true) =>
        new(owner.Id, "مورد", titleEn, null, null, type, levels ?? [], courseId, tags ?? [], null, fileAssetId, type == LibraryResourceTypes.Link ? url : null,
            downloadAllowed);

    public async Task<LibraryResourceDto> CreateAsync(RoleAssignment who, SaveLibraryResourceRequest request)
    {
        var response = await SendAsync(who, HttpMethod.Post, Routes.Library, request);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<LibraryResourceDto>(response);
    }

    public async Task<PagedResult<LibraryResourceDto>> ListAsync(RoleAssignment who, string query = "") =>
        await ReadAsync<PagedResult<LibraryResourceDto>>(await SendAsync(who, HttpMethod.Get, Routes.Library + query));

    /// <summary>A real stored PDF and its asset, owned by the unit (through a class course of that unit).</summary>
    public Task<Guid> UploadAssetAsync(OrgUnit unit, string fileName = "book.pdf") => UploadAssetAsync(_factory, Tree.Root, unit, fileName);

    public static async Task<Guid> UploadAssetAsync(LmsApiFactory factory, OrgUnit root, OrgUnit unit, string fileName = "book.pdf")
    {
        var ctx = TestCurrentContext.For(unit.OrganizationId, root.Path);
        await using var db = factory.CreateDbContext(ctx);
        var options = factory.Services.GetRequiredService<IOptions<StorageOptions>>();
        var storage = new MinioFileStorage(db, new SettingsResolver(db, ctx), new MinioClients(options), options, new SystemClock());
        var stored = await storage.UploadAsync(new FileUploadRequest(fileName, "application/pdf", Pdf.Length, unit.Id), new MemoryStream(Pdf));

        var year = await AcademicTestKit.AddYearAsync(factory, root);
        var cls = await AcademicTestKit.AddClassAsync(factory, unit, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, unit);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        await using var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var asset = new Asset(classCourse, stored.Id, AssetKinds.Document, fileName, "application/pdf", Pdf.Length);
        write.Add(asset);
        await write.SaveChangesAsync();
        return asset.Id;
    }

    private async Task SeedAsync()
    {
        Admin = await AccessTestData.AssignAsync(_factory, Tree, Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        School1Admin = await AccessTestData.AssignAsync(_factory, Tree, Tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        School2Admin = await AccessTestData.AssignAsync(_factory, Tree, Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacherUser = await PeopleTestKit.AddUserAsync(_factory);
        Teacher = await AccessTestData.AssignAsync(_factory, Tree, Tree.School1, BuiltInRoles.Teacher, teacherUser.Id);
        var studentUser = await PeopleTestKit.AddUserAsync(_factory);
        var student = await PeopleTestKit.AddStudentAsync(_factory, Tree.School1, PeopleTestKit.NewCode());
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id)).LinkUser(studentUser.Id);
            var other = new LevelType(Tree.Org.Id, "مستوى آخر", "Other level", 99, false);
            db.Add(other);
            OtherLevel = other.Id;
            await db.SaveChangesAsync();
        }

        Student = await AccessTestData.AssignAsync(_factory, Tree, Tree.School1, BuiltInRoles.Student, studentUser.Id);
    }
}
