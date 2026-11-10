using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;

namespace Lms.IntegrationTests.Files;

[Collection(ApiCollection.Name)]
public sealed class FileEndpointTests(LmsApiFactory factory)
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("%PDF-1.7 تقرير 🌟 endpoint bytes");

    private static HttpRequestMessage Upload(
        RoleAssignment? who, Guid unitId, string fileName = "report.pdf", byte[]? bytes = null,
        string declaredContentType = "application/pdf")
    {
        var form = new MultipartFormDataContent { { new StringContent(unitId.ToString()), "orgUnitId" } };
        var file = new ByteArrayContent(bytes ?? Content);
        file.Headers.ContentType = new MediaTypeHeaderValue(declaredContentType);
        form.Add(file, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/files") { Content = form };
        As(request, who);
        return request;
    }

    private static HttpRequestMessage Url(RoleAssignment? who, Guid fileId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/url");
        As(request, who);
        return request;
    }

    private static void As(HttpRequestMessage request, RoleAssignment? who)
    {
        if (who is null)
        {
            return;
        }

        request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, who.Id.ToString());
    }

    private async Task<OrgUnit> AddSchoolAsync(AccessTree tree, string code)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var levelId = db.LevelTypes.Single(x => x.OrganizationId == tree.Org.Id).Id;
        var school = OrgUnit.CreateChild(tree.Root, levelId, "مدرسة " + code, "School " + code, code);
        db.Add(school);
        await db.SaveChangesAsync();
        return school;
    }

    [Fact]
    public async Task Upload_then_presigned_url_downloads_same_bytes()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE1");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var client = factory.CreateClient();

        var upload = await client.SendAsync(Upload(teacher, tree.School1.Id, "تقرير.pdf"));

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var created = await upload.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("تقرير.pdf", created.GetProperty("fileName").GetString());
        Assert.Equal(Content.Length, created.GetProperty("sizeBytes").GetInt64());

        var urlResponse = await client.SendAsync(Url(teacher, created.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, urlResponse.StatusCode);
        var url = (await urlResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString();

        using var plain = new HttpClient();
        Assert.Equal(Content, await plain.GetByteArrayAsync(url));
    }

    [Fact]
    public async Task Upload_over_max_size_is_rejected_413()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE2");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var admin = TestCurrentContext.PlatformAdmin();
        await using (var db = factory.CreateDbContext(admin))
        {
            await new SettingsResolver(db, admin).SetAsync(SettingKeys.FilesMaxUploadMb, tree.Root.Id, 1, isLocked: false);
        }

        var response = await factory.CreateClient().SendAsync(
            Upload(teacher, tree.School1.Id, bytes: new byte[2 * 1024 * 1024]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Disallowed_extension_is_rejected_415()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE3");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        var response = await factory.CreateClient().SendAsync(Upload(teacher, tree.School1.Id, "script.exe"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Other_client_cannot_get_url_for_file()
    {
        var a = await AccessTestData.SeedAsync(factory, "FE4A");
        var b = await AccessTestData.SeedAsync(factory, "FE4B");
        var teacherA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var teacherB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var client = factory.CreateClient();
        var created = await (await client.SendAsync(Upload(teacherA, a.School1.Id))).Content.ReadFromJsonAsync<JsonElement>();

        var response = await client.SendAsync(Url(teacherB, created.GetProperty("id").GetGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Sibling_unit_scope_cannot_get_url_for_file()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE5");
        var school2 = await AddSchoolAsync(tree, "S2");
        var teacher1 = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var teacher2 = await AccessTestData.AssignAsync(factory, tree, school2, BuiltInRoles.Teacher, Guid.NewGuid());
        var client = factory.CreateClient();
        var created = await (await client.SendAsync(Upload(teacher1, tree.School1.Id))).Content.ReadFromJsonAsync<JsonElement>();

        var response = await client.SendAsync(Url(teacher2, created.GetProperty("id").GetGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_into_a_unit_outside_the_scope_is_404()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE6");
        var school2 = await AddSchoolAsync(tree, "S2");
        var teacher1 = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        var response = await factory.CreateClient().SendAsync(Upload(teacher1, school2.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_requires_the_upload_permission_and_download_requires_the_download_permission()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE7");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var guardian = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, Guid.NewGuid());
        var client = factory.CreateClient();

        // The guardian can download but not upload.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Upload(guardian, tree.School1.Id))).StatusCode);
        var created = await (await client.SendAsync(Upload(teacher, tree.School1.Id))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Url(guardian, created.GetProperty("id").GetGuid()))).StatusCode);

        // Anonymous callers get nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Upload(null, tree.School1.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Url(null, Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Missing_file_part_is_a_400()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE8");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/files")
        {
            Content = new MultipartFormDataContent { { new StringContent(tree.School1.Id.ToString()), "orgUnitId" } },
        };
        As(request, teacher);

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }

    private static HttpRequestMessage MalformedUpload(RoleAssignment? who)
    {
        var content = new ByteArrayContent("this is not a multipart body"u8.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=nope");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/files") { Content = content };
        As(request, who);
        return request;
    }

    [Fact]
    public async Task Anonymous_upload_is_401_before_the_body_is_read()
    {
        var response = await factory.CreateClient().SendAsync(MalformedUpload(null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_without_permission_is_403_even_with_a_malformed_form()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE9");
        var guardian = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, Guid.NewGuid());

        var response = await factory.CreateClient().SendAsync(MalformedUpload(guardian));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_form_from_an_allowed_caller_is_400()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE10");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        var response = await factory.CreateClient().SendAsync(MalformedUpload(teacher));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_declared_content_type_is_ignored_and_download_is_an_attachment_with_the_original_name()
    {
        var tree = await AccessTestData.SeedAsync(factory, "FE11");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var client = factory.CreateClient();
        var html = Encoding.UTF8.GetBytes("<script>alert(1)</script>");

        var upload = await client.SendAsync(Upload(teacher, tree.School1.Id, "تقرير.png", html, "text/html"));
        var created = await upload.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("image/png", created.GetProperty("contentType").GetString());

        var url = (await (await client.SendAsync(Url(teacher, created.GetProperty("id").GetGuid())))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString();
        using var plain = new HttpClient();
        var download = await plain.GetAsync(url);

        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        var disposition = download.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("تقرير.png", disposition.FileNameStar);
    }
}
