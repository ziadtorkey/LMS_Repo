using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Content;

/// <summary>Reviewer-added cases for the asset-backed modules (M4a Task 5): deletion, schedule windows, foreign assets on update.</summary>
[Collection(ApiCollection.Name)]
public sealed class AssetModuleExtraTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);
    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Student_loses_access_when_module_is_deleted_and_other_teacher_never_had_it()
    {
        var w = await _t.SetUpAsync("QAM1");
        var asset = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var module = await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, asset.Id));
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(asset.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.AssetUrl(asset.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.Asset(asset.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Admin, HttpMethod.Get, Routes.AssetUrl(asset.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.Module(module.Node.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(asset.Id))).StatusCode);
    }

    [Fact]
    public async Task Scheduled_in_future_and_expired_windows_hide_the_asset_from_students()
    {
        var w = await _t.SetUpAsync("QAM2");
        var future = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var past = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id, "p.pdf");
        var now = DateTime.UtcNow;
        foreach (var (a, from, to) in new[] { (future, now.AddDays(1), now.AddDays(2)), (past, now.AddDays(-2), now.AddDays(-1)) })
        {
            var req = new CreateModuleRequest(ModuleKinds.File, "ملف", "F", ModuleVisibilities.Scheduled, from, to,
                ModuleTargets.ClassCourse, null, CompletionRules.None, null, new ModuleContentInput(AssetId: a.Id));
            await _t.CreateAsync(w.Teacher, w, req);
            Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(a.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.AssetUrl(a.Id))).StatusCode);
        }
    }

    [Fact]
    public async Task Update_cannot_point_a_module_at_an_asset_of_another_class_course()
    {
        var w = await _t.SetUpAsync("QAM3");
        Guid otherCc;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            otherCc = (await db.Set<ClassCourseTeacher>().SingleAsync(t => t.UserId == w.OtherTeacher.UserId)).ClassCourseId;
        }
        var foreign = await _t.UploadAssetAsync(w.Admin, otherCc);
        var own = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var module = await _t.CreateAsync(w.Teacher, w, AssetModule(ModuleKinds.File, own.Id));
        var update = new UpdateModuleRequest("ملف", "R", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null,
            CompletionRules.None, null, new ModuleContentInput(AssetId: foreign.Id));
        var r = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Module(module.Node.Id), update);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        // and the foreign asset is still unreadable to the first teacher
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.AssetUrl(foreign.Id))).StatusCode);
    }
}
