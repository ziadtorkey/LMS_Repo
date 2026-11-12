using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Content;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Content;

/// <summary>M4a Task 5 (FR-CNT-01, FR-CNT-03): File, Video and Audio modules that point at an asset of the same class course.</summary>
[Collection(ApiCollection.Name)]
public sealed class AssetModuleTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Video_module_requires_a_video_asset()
    {
        var w = await _t.SetUpAsync("AM1");
        var pdf = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var mp4 = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id, "lesson.mp4", "video/mp4");

        var wrong = await _t.CreateRawAsync(w.Teacher, w, AssetModule(ModuleKinds.Video, pdf.Id));
        var created = await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.Video, mp4.Id));

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(ProblemCodes.asset_type_not_allowed, await CodeAsync(wrong));
        Assert.Equal(mp4.Id, created.Content.Asset?.Id);
        Assert.Equal(AssetKinds.Video, created.Content.Asset?.Kind);
    }

    [Fact]
    public async Task File_module_accepts_pdf_and_image_and_audio_module_only_audio()
    {
        var w = await _t.SetUpAsync("AM2");
        var pdf = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var png = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id, "pic.png", "image/png");
        var mp3 = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id, "talk.mp3", "audio/mpeg");

        Assert.Equal(pdf.Id, (await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, pdf.Id))).Content.Asset?.Id);
        Assert.Equal(png.Id, (await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, png.Id))).Content.Asset?.Id);
        Assert.Equal(mp3.Id, (await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.Audio, mp3.Id))).Content.Asset?.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.CreateRawAsync(w.Teacher, w, AssetModule(ModuleKinds.File, mp3.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.CreateRawAsync(w.Teacher, w, AssetModule(ModuleKinds.Audio, pdf.Id))).StatusCode);
    }

    [Fact]
    public async Task Missing_unknown_and_other_class_course_assets_are_rejected_and_page_refuses_an_asset()
    {
        var w = await _t.SetUpAsync("AM3");
        Guid otherClassCourseId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            otherClassCourseId = (await db.Set<ClassCourseTeacher>().SingleAsync(t => t.UserId == w.OtherTeacher.UserId)).ClassCourseId;
        }

        var other = await _t.UploadAssetAsync(w.Admin, otherClassCourseId);

        foreach (var request in new[]
                 {
                     AssetModule(ModuleKinds.File, null),
                     AssetModule(ModuleKinds.File, Guid.NewGuid()),
                     AssetModule(ModuleKinds.File, other.Id),
                 })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _t.CreateRawAsync(w.Teacher, w, request)).StatusCode);
        }

        var page = Page() with { Content = new ModuleContentInput(AssetId: other.Id, BodyHtmlEn: "<p>x</p>") };
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.CreateRawAsync(w.Teacher, w, page)).StatusCode);
    }

    [Fact]
    public async Task Update_swaps_the_asset_and_deleting_the_module_keeps_the_asset()
    {
        var w = await _t.SetUpAsync("AM4");
        var first = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var second = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id, "b.pdf");
        var module = await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, first.Id));

        var update = new UpdateModuleRequest(
            "ملف", "Renamed", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null,
            new ModuleContentInput(AssetId: second.Id));
        var updated = (await (await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Module(module.Node.Id), update)).Content
            .ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal(second.Id, updated.Content.Asset?.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.Module(module.Node.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Asset(second.Id))).StatusCode);
    }

    [Fact]
    public async Task Student_detail_includes_asset_and_url_is_issued_only_for_visible_modules()
    {
        var w = await _t.SetUpAsync("AM5");
        var visible = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var hidden = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id, "hidden.pdf");
        var unused = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id, "unused.pdf");
        var module = await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, visible.Id));
        await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, hidden.Id, ModuleVisibilities.Hidden));

        var detail = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(module.Node.Id))).Content
            .ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal(visible.Id, detail.Content.Asset?.Id);

        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(visible.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Asset(visible.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(hidden.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(unused.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Stranger, HttpMethod.Get, Routes.AssetUrl(visible.Id))).StatusCode);
    }

    [Fact]
    public async Task Group_targeted_asset_is_hidden_from_students_outside_the_group()
    {
        var w = await _t.SetUpAsync("AM6");
        var asset = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var group = await _t.AddGroupAsync(w.ClassCourse);
        await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, asset.Id, target: ModuleTargets.Group, groupId: group.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(asset.Id))).StatusCode);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<ModuleContent>().CountAsync(c => c.AssetId == asset.Id));
    }
}
