using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Content;

/// <summary>M4a Task 4 (FR-CNT-03, X-05): asset upload, limits, metadata and signed URLs.</summary>
[Collection(ApiCollection.Name)]
public sealed class AssetTests(LmsApiFactory factory) : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 asset bytes");
    private readonly List<OrgUnitTestHost> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private sealed record Setup(EnrollmentWorld World, RoleAssignment Admin, RoleAssignment Teacher, OrgUnitTestHost Host);

    /// <summary>A class with courses A and B; a teacher teaching course A only, and an org admin.</summary>
    private async Task<Setup> SetUpAsync(string name, Action<IServiceCollection>? configure = null)
    {
        var host = new OrgUnitTestHost(factory, configure);
        _hosts.Add(host);
        var w = await EnrollmentWorld.CreateAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(
            factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var ccA = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcA.Id);
        db.Add(new ClassCourseTeacher(ccA, teacher.UserId, ClassCourseTeacher.PrimaryRole, Today));
        await db.SaveChangesAsync();
        return new Setup(w, admin, teacher, host);
    }

    private static HttpRequestMessage Upload(
        Guid classCourseId, string fileName = "notes.pdf", string contentType = "application/pdf", byte[]? bytes = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(classCourseId.ToString()), AssetUploadForm.ClassCourseId } };
        var file = new ByteArrayContent(bytes ?? Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, AssetUploadForm.File, fileName);
        return new HttpRequestMessage(HttpMethod.Post, Routes.Assets) { Content = form };
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<AssetDto> UploadOkAsync(Setup s, Guid classCourseId, string fileName = "notes.pdf", string contentType = "application/pdf", byte[]? bytes = null)
    {
        var response = await s.Host.SendRawAsync(Upload(classCourseId, fileName, contentType, bytes), s.Teacher);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetDto>())!;
    }

    [Fact]
    public async Task Teacher_uploads_a_pdf_and_reads_its_metadata_and_signed_url()
    {
        var s = await SetUpAsync("AS1");

        var asset = await UploadOkAsync(s, s.World.CcA.Id);

        Assert.Equal(AssetKinds.Document, asset.Kind);
        Assert.Equal("notes.pdf", asset.FileName);
        Assert.Equal(Pdf.Length, asset.SizeBytes);
        Assert.True(asset.Previewable);
        Assert.Equal(s.World.CcA.Id, asset.ClassCourseId);

        var meta = await s.Host.SendAsync(HttpMethod.Get, Routes.Asset(asset.Id), s.Teacher);
        Assert.Equal(HttpStatusCode.OK, meta.StatusCode);

        var url = await s.Host.SendAsync(HttpMethod.Get, Routes.AssetUrl(asset.Id), s.Teacher);
        Assert.Equal(HttpStatusCode.OK, url.StatusCode);
        var dto = (await url.Content.ReadFromJsonAsync<AssetUrlDto>())!;
        Assert.True(dto.ExpiresAtUtc > dto.ServerNowUtc);
        using var download = await new HttpClient().GetAsync(dto.Url);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("inline", download.Content.Headers.ContentDisposition?.ToString());
    }

    [Theory]
    [InlineData("drawing.svg", "image/svg+xml")]
    [InlineData("page.html", "text/html")]
    [InlineData("pack.zip", "application/zip")]
    [InlineData("noextension", "application/pdf")]
    [InlineData("tool.exe", "application/octet-stream")]
    public async Task Rejects_svg_html_zip_and_unknown_extensions(string fileName, string contentType)
    {
        var s = await SetUpAsync("AS2");

        var response = await s.Host.SendRawAsync(Upload(s.World.CcA.Id, fileName, contentType), s.Teacher);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.asset_type_not_allowed, await CodeAsync(response));
    }

    [Fact]
    public async Task Rejects_content_type_mismatch()
    {
        var s = await SetUpAsync("AS3");

        var response = await s.Host.SendRawAsync(Upload(s.World.CcA.Id, "photo.png", "text/html"), s.Teacher);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.asset_type_not_allowed, await CodeAsync(response));
    }

    [Fact]
    public async Task Rejects_file_over_effective_limit()
    {
        var s = await SetUpAsync("AS4");
        await SetLimitMbAsync(s.World.Tree.Root.Id, 1);

        var response = await s.Host.SendRawAsync(Upload(s.World.CcA.Id, bytes: new byte[2 * 1024 * 1024]), s.Teacher);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.asset_too_large, await CodeAsync(response));
    }

    [Fact]
    public async Task Limits_follow_unit_setting()
    {
        var s = await SetUpAsync("AS5");
        await SetLimitMbAsync(s.World.Tree.Root.Id, 3);

        var response = await s.Host.SendAsync(HttpMethod.Get, Routes.AssetsLimitsFor(s.World.CcA.Id), s.Teacher);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var limits = (await response.Content.ReadFromJsonAsync<AssetLimitsDto>())!;
        Assert.Equal(3L * 1024 * 1024, limits.MaxBytes);
        Assert.Equal(AssetTypes.All.Count, limits.AllowedTypes.Count);
    }

    [Fact]
    public async Task Teacher_of_another_class_course_gets_404()
    {
        var s = await SetUpAsync("AS6");

        var upload = await s.Host.SendRawAsync(Upload(s.World.CcB.Id), s.Teacher);
        var limits = await s.Host.SendAsync(HttpMethod.Get, Routes.AssetsLimitsFor(s.World.CcB.Id), s.Teacher);

        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, limits.StatusCode);

        var asset = (await (await s.Host.SendRawAsync(Upload(s.World.CcB.Id), s.Admin)).Content.ReadFromJsonAsync<AssetDto>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await s.Host.SendAsync(HttpMethod.Get, Routes.Asset(asset.Id), s.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Host.SendAsync(HttpMethod.Get, Routes.AssetUrl(asset.Id), s.Teacher)).StatusCode);
    }

    [Fact]
    public async Task Admin_of_a_sibling_school_and_of_another_client_get_404()
    {
        var s = await SetUpAsync("AS7");
        var asset = await UploadOkAsync(s, s.World.CcA.Id);
        var school2Admin = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var otherTree = await AccessTestData.SeedAsync(factory, "AS7B");
        var otherAdmin = await AccessTestData.AssignAsync(factory, otherTree, otherTree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        foreach (var caller in new[] { school2Admin, otherAdmin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await s.Host.SendAsync(HttpMethod.Get, Routes.Asset(asset.Id), caller)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await s.Host.SendAsync(HttpMethod.Get, Routes.AssetUrl(asset.Id), caller)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await s.Host.SendRawAsync(Upload(s.World.CcA.Id), caller)).StatusCode);
        }
    }

    [Fact]
    public async Task Signed_url_requires_a_reference_the_caller_can_see()
    {
        var allow = new SwitchableReader();
        var s = await SetUpAsync("AS8", services => services.AddSingleton<IAssetReferenceReader>(allow));
        var asset = await UploadOkAsync(s, s.World.CcA.Id);
        var student = await AccessTestData.AssignAsync(
            factory, s.World.Tree, s.World.Tree.School1, BuiltInRoles.Student, (await PeopleTestKit.AddUserAsync(factory)).Id);

        var before = await s.Host.SendAsync(HttpMethod.Get, Routes.AssetUrl(asset.Id), student);
        allow.Allowed = true;
        var after = await s.Host.SendAsync(HttpMethod.Get, Routes.AssetUrl(asset.Id), student);

        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Video_url_supports_range_requests()
    {
        var s = await SetUpAsync("AS9");
        var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        var asset = await UploadOkAsync(s, s.World.CcA.Id, "lesson.mp4", "video/mp4", bytes);
        Assert.Equal(AssetKinds.Video, asset.Kind);

        var url = (await (await s.Host.SendAsync(HttpMethod.Get, Routes.AssetUrl(asset.Id), s.Teacher))
            .Content.ReadFromJsonAsync<AssetUrlDto>())!;
        using var request = new HttpRequestMessage(HttpMethod.Get, url.Url);
        request.Headers.Range = new RangeHeaderValue(100, 199);
        using var ranged = await new HttpClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, ranged.StatusCode);
        Assert.Equal(bytes[100..200], await ranged.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Closed_class_rejects_uploads()
    {
        var s = await SetUpAsync("AS10");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Class>().SingleAsync(c => c.Id == s.World.Class.Id);
            db.Entry(cls).Property(nameof(Class.Status)).CurrentValue = Class.ClosedStatus;
            await db.SaveChangesAsync();
        }

        var response = await s.Host.SendRawAsync(Upload(s.World.CcA.Id), s.Teacher);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.class_closed, await CodeAsync(response));
    }

    private async Task SetLimitMbAsync(Guid unitId, int mb)
    {
        var admin = TestCurrentContext.PlatformAdmin();
        await using var db = factory.CreateDbContext(admin);
        await new SettingsResolver(db, admin).SetAsync(SettingKeys.FilesMaxUploadMb, unitId, mb, isLocked: false);
    }

    private sealed class SwitchableReader : IAssetReferenceReader
    {
        public bool Allowed { get; set; }

        public Task<bool> CanReadAsync(Asset asset, CancellationToken cancellationToken = default) => Task.FromResult(Allowed);
    }
}
