using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Content;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Packages;

/// <summary>
/// M4b Task 8a: the package routes (upload, unattached list, status, attach, delete). The flag, the separate content origin, the closed
/// class guard, the relationship rules (404) and the permission split (a student has no Packages.Upload) are each pinned here; the
/// archive rules are in <see cref="ZipHardeningTests"/>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PackageUploadTests(LmsApiFactory factory) : IDisposable
{
    private const string ContentOrigin = "http://127.0.0.1:5999";

    private readonly List<ContentTestHost> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private sealed record Setup(ContentWorld World, ContentTestHost Host, OrgUnitTestHost Api);

    private async Task<Setup> SetUpAsync(string name, string? origin = ContentOrigin, bool flag = true)
    {
        var host = new ContentTestHost(factory);
        _hosts.Add(host);
        var world = await host.SetUpAsync(name);
        var api = new OrgUnitTestHost(factory, services => services.Configure<PackageHostOptions>(o => o.Origin = origin));
        if (flag)
        {
            await SetAsync(world, FeatureFlags.ScormContent.Name, "true");
        }

        return new Setup(world, host, api);
    }

    private async Task SetAsync(ContentWorld w, string key, string json)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(w.Tree.Org.Id, w.Tree.Root.Id, w.Tree.Root.Path, key, json, false));
        await db.SaveChangesAsync();
    }

    private static readonly byte[] Zip = [(byte)'P', (byte)'K', 3, 4, 1, 2, 3, 4];

    private static HttpRequestMessage Upload(
        Guid classCourseId, Guid? lessonKey, string kind = PackageKinds.Scorm, string fileName = "course.zip", byte[]? bytes = null,
        bool lessonFirst = true)
    {
        var form = new MultipartFormDataContent();
        if (lessonFirst && lessonKey is { } lesson)
        {
            form.Add(new StringContent(lesson.ToString()), PackageUploadForm.LessonKey);
        }

        form.Add(new StringContent(kind), PackageUploadForm.Kind);
        var file = new ByteArrayContent(bytes ?? Zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (lessonFirst)
        {
            form.Add(file, PackageUploadForm.File, fileName);
        }
        else
        {
            // The lesson after the file: the checks that need it have already run, so the form is refused.
            form.Add(file, PackageUploadForm.File, fileName);
            form.Add(new StringContent((lessonKey ?? Guid.NewGuid()).ToString()), PackageUploadForm.LessonKey);
        }

        return new HttpRequestMessage(HttpMethod.Post, Routes.ClassCoursePackages(classCourseId)) { Content = form };
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<PackageUploadDto> UploadOkAsync(Setup s, string kind = PackageKinds.Scorm, string fileName = "course.zip")
    {
        var response = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1, kind, fileName), s.World.Teacher);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PackageUploadDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private async Task MarkReadyAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.Set<PackageUpload>().SingleAsync(p => p.Id == id)).MarkReady("Course title", ScormVersions.Scorm12, null, "p/");
        await db.SaveChangesAsync();
    }

    private static AttachPackageModuleRequest Attach(string rule = CompletionRules.None) =>
        new(new ModuleSettingsInput("حزمة", "Package", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, rule, null));

    [Fact]
    public async Task Teacher_uploads_a_scorm_zip_and_the_upload_stays_uploaded()
    {
        var s = await SetUpAsync("PK1");

        var upload = await UploadOkAsync(s);

        Assert.Equal((PackageKinds.Scorm, PackageUploadStatuses.Uploaded, "course.zip"), (upload.Kind, upload.Status, upload.FileName));
        Assert.Equal((s.World.ClassCourse.Id, ContentTestHost.Lesson1), (upload.ClassCourseId, upload.LessonKey));
        Assert.Null(upload.ModuleId);
        Assert.Null(upload.RejectReason);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.Set<PackageUpload>().SingleAsync(p => p.Id == upload.Id);
        Assert.Equal(s.World.ClassCourse.OrgUnitId, row.OrgUnitId);
        Assert.Equal(64, row.Sha256.Length);
        Assert.Equal(Zip.Length, row.SizeBytes);
        Assert.True(await db.Set<Lms.Domain.Files.StoredFile>().AnyAsync(f => f.Id == row.ArchiveFileId));
    }

    [Fact]
    public async Task H5p_upload_needs_the_h5p_extension_and_scorm_the_zip_extension()
    {
        var s = await SetUpAsync("PK2");

        var h5p = await UploadOkAsync(s, PackageKinds.H5p, "quiz.h5p");
        var wrongForScorm = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1, PackageKinds.Scorm, "quiz.h5p"), s.World.Teacher);
        var wrongForH5p = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1, PackageKinds.H5p, "course.zip"), s.World.Teacher);
        var unknownKind = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1, "Flash", "x.zip"), s.World.Teacher);

        Assert.Equal(PackageKinds.H5p, h5p.Kind);
        Assert.All(new[] { wrongForScorm, wrongForH5p, unknownKind }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task The_lesson_must_come_before_the_file_and_belong_to_the_class_course_plan()
    {
        var s = await SetUpAsync("PK3");

        var lessonAfterFile = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1, lessonFirst: false), s.World.Teacher);
        var noLesson = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, null), s.World.Teacher);
        var unknownLesson = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, Guid.NewGuid()), s.World.Teacher);

        Assert.Equal(HttpStatusCode.BadRequest, lessonAfterFile.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noLesson.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownLesson.StatusCode);
    }

    [Fact]
    public async Task An_oversized_text_part_is_refused_without_reading_it_all()
    {
        var s = await SetUpAsync("PK4B");
        var form = new MultipartFormDataContent
        {
            { new StringContent(new string('9', 5 * 1024 * 1024)), PackageUploadForm.LessonKey },
            { new StringContent(PackageKinds.Scorm), PackageUploadForm.Kind },
        };
        form.Add(new ByteArrayContent(Zip), PackageUploadForm.File, "course.zip");
        var request = new HttpRequestMessage(HttpMethod.Post, Routes.ClassCoursePackages(s.World.ClassCourse.Id)) { Content = form };

        var response = await s.Api.SendRawAsync(request, s.World.Teacher);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await CodeAsync(response));
        // The bound is what refused it, not a later "not a guid" check on a fully buffered string.
        Assert.Contains("form_part_too_large", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_form_with_too_many_text_parts_is_refused()
    {
        var s = await SetUpAsync("PK4C");
        var form = new MultipartFormDataContent();
        for (var i = 0; i < 6; i++)
        {
            form.Add(new StringContent("x"), "extra" + i);
        }

        form.Add(new StringContent(ContentTestHost.Lesson1.ToString()), PackageUploadForm.LessonKey);
        form.Add(new StringContent(PackageKinds.Scorm), PackageUploadForm.Kind);
        form.Add(new ByteArrayContent(Zip), PackageUploadForm.File, "course.zip");
        var request = new HttpRequestMessage(HttpMethod.Post, Routes.ClassCoursePackages(s.World.ClassCourse.Id)) { Content = form };

        var response = await s.Api.SendRawAsync(request, s.World.Teacher);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hidden_characters_are_stripped_from_the_display_name()
    {
        var s = await SetUpAsync("PK4D");

        var upload = await UploadOkAsync(s, PackageKinds.Scorm, "co\u202Eurse\u061C.zip");

        Assert.Equal("course.zip", upload.FileName);
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var s = await SetUpAsync("PK4");

        var response = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1, bytes: []), s.World.Teacher);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_archive_over_the_unit_upload_limit_is_413_package_too_large()
    {
        var s = await SetUpAsync("PK5");
        await SetAsync(s.World, SettingKeys.FilesMaxUploadMb.Name, "1");

        var response = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1, bytes: new byte[1024 * 1024 + 1]), s.World.Teacher);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("package_too_large", await CodeAsync(response));
    }

    [Fact]
    public async Task Flag_off_refuses_upload()
    {
        var s = await SetUpAsync("PK6", flag: false);

        var response = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1), s.World.Teacher);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("feature_disabled", await CodeAsync(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://localhost")]
    [InlineData("http://content.localhost:5999")]
    public async Task Upload_refuses_without_a_separate_content_origin(string? origin)
    {
        // The test server answers as http://localhost: missing, malformed, equal and sibling hosts are all the same site, so every route fails closed.
        var s = await SetUpAsync("PK7", origin);

        var response = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1), s.World.Teacher);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("package_host_not_configured", await CodeAsync(response));
    }

    [Fact]
    public async Task Closed_class_refuses_upload()
    {
        var s = await SetUpAsync("PK8");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == s.World.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var response = await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1), s.World.Teacher);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("class_closed", await CodeAsync(response));
    }

    [Fact]
    public async Task Teacher_of_other_class_course_gets_404_and_a_student_is_403()
    {
        var s = await SetUpAsync("PK9");
        var upload = await UploadOkAsync(s);

        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1), s.World.OtherTeacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendAsync(HttpMethod.Get, Routes.Package(upload.Id), s.World.OtherTeacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendAsync(HttpMethod.Delete, Routes.Package(upload.Id), s.World.OtherTeacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.OtherTeacher, Attach())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackages(s.World.ClassCourse.Id), s.World.OtherTeacher)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Api.SendRawAsync(Upload(s.World.ClassCourse.Id, ContentTestHost.Lesson1), s.World.Student)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Api.SendAsync(HttpMethod.Get, Routes.Package(upload.Id), s.World.Student)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackages(s.World.ClassCourse.Id), s.World.Student)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.Student, Attach())).StatusCode);
    }

    [Fact]
    public async Task Another_client_cannot_see_the_package()
    {
        var s = await SetUpAsync("PK10");
        var upload = await UploadOkAsync(s);
        var other = await AccessTestData.SeedAsync(factory, "PK10B");
        var admin = await AccessTestData.AssignAsync(factory, other, other.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendAsync(HttpMethod.Get, Routes.Package(upload.Id), admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendAsync(HttpMethod.Delete, Routes.Package(upload.Id), admin)).StatusCode);
    }

    [Fact]
    public async Task Staff_read_the_status_and_the_unattached_list_per_lesson()
    {
        var s = await SetUpAsync("PK11");
        var upload = await UploadOkAsync(s);

        var status = await s.Api.SendAsync(HttpMethod.Get, Routes.Package(upload.Id), s.World.Teacher);
        var list = await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackages(s.World.ClassCourse.Id), s.World.Admin);
        var lesson1 = await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackagesOfLesson(s.World.ClassCourse.Id, ContentTestHost.Lesson1), s.World.Teacher);
        var lesson2 = await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackagesOfLesson(s.World.ClassCourse.Id, ContentTestHost.Lesson2), s.World.Teacher);
        var validating = await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackages(s.World.ClassCourse.Id) + "?status=Validating", s.World.Teacher);
        var invalid = await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackages(s.World.ClassCourse.Id) + "?status=Nope", s.World.Teacher);

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(PackageUploadStatuses.Uploaded, (await status.Content.ReadFromJsonAsync<PackageUploadDto>())!.Status);
        Assert.Equal([upload.Id], (await list.Content.ReadFromJsonAsync<List<PackageUploadDto>>())!.Select(p => p.Id));
        Assert.Equal([upload.Id], (await lesson1.Content.ReadFromJsonAsync<List<PackageUploadDto>>())!.Select(p => p.Id));
        Assert.Empty((await lesson2.Content.ReadFromJsonAsync<List<PackageUploadDto>>())!);
        Assert.Empty((await validating.Content.ReadFromJsonAsync<List<PackageUploadDto>>())!);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Attach_requires_ready_and_then_adds_the_module_once()
    {
        var s = await SetUpAsync("PK12");
        var upload = await UploadOkAsync(s);

        var notReady = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.Teacher, Attach());
        Assert.Equal(HttpStatusCode.Conflict, notReady.StatusCode);
        Assert.Equal("package_not_ready", await CodeAsync(notReady));

        await MarkReadyAsync(upload.Id);
        var badRule = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.Teacher, Attach(CompletionRules.Manual));
        var attached = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.Teacher, Attach(CompletionRules.Submit));
        var again = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.Teacher, Attach());

        Assert.Equal(HttpStatusCode.BadRequest, badRule.StatusCode);
        Assert.Equal(HttpStatusCode.Created, attached.StatusCode);
        var node = (await attached.Content.ReadFromJsonAsync<ModuleNodeDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        Assert.Equal((ModuleKinds.Scorm, CompletionRules.Submit), (node.Kind, node.CompletionRule));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var module = await db.Set<LessonModule>().SingleAsync(m => m.Id == node.Id);
        Assert.Equal((upload.Id, ContentTestHost.Lesson1), (module.RefId, module.LessonKey));
        Assert.Equal(node.Id, (await db.Set<PackageUpload>().SingleAsync(p => p.Id == upload.Id)).ModuleId);
        var unattached = await s.Api.SendAsync(HttpMethod.Get, Routes.ClassCoursePackages(s.World.ClassCourse.Id), s.World.Teacher);
        Assert.Empty((await unattached.Content.ReadFromJsonAsync<List<PackageUploadDto>>())!);
    }

    [Fact]
    public async Task H5p_package_attaches_as_an_h5p_module()
    {
        var s = await SetUpAsync("PK13");
        var upload = await UploadOkAsync(s, PackageKinds.H5p, "quiz.h5p");
        await MarkReadyAsync(upload.Id);

        var response = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.Teacher, Attach(CompletionRules.Score));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ModuleKinds.H5p, (await response.Content.ReadFromJsonAsync<ModuleNodeDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.Kind);
    }

    [Fact]
    public async Task Delete_removes_the_upload_its_module_and_the_archive()
    {
        var s = await SetUpAsync("PK14");
        var upload = await UploadOkAsync(s);
        await MarkReadyAsync(upload.Id);
        var attached = await s.Api.SendAsync(HttpMethod.Post, Routes.PackageModule(upload.Id), s.World.Teacher, Attach());
        var moduleId = (await attached.Content.ReadFromJsonAsync<ModuleNodeDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.Id;

        var deleted = await s.Api.SendAsync(HttpMethod.Delete, Routes.Package(upload.Id), s.World.Teacher);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Api.SendAsync(HttpMethod.Get, Routes.Package(upload.Id), s.World.Teacher)).StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<LessonModule>().AnyAsync(m => m.Id == moduleId));
        var row = await db.Set<PackageUpload>().IgnoreQueryFilters().SingleAsync(p => p.Id == upload.Id);
        Assert.True(row.IsDeleted);
        Assert.False(await db.Set<Lms.Domain.Files.StoredFile>().AnyAsync(f => f.Id == row.ArchiveFileId));
        Assert.True(await db.Set<PackageRevocation>().AnyAsync(r => r.PackageId == upload.Id && r.StudentProfileId == null)); // the content host refuses tokens issued before the delete
    }

    [Fact]
    public async Task Delete_of_a_package_in_a_closed_class_is_refused()
    {
        var s = await SetUpAsync("PK15");
        var upload = await UploadOkAsync(s);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == s.World.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var response = await s.Api.SendAsync(HttpMethod.Delete, Routes.Package(upload.Id), s.World.Teacher);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

}
