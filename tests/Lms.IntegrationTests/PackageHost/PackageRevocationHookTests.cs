using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>Hiding a package module and ending an enrollment revoke the tokens already issued (ADR 0005 section 5, T21 4.5).</summary>
[Collection(ApiCollection.Name)]
public sealed class PackageRevocationHookTests(LmsApiFactory factory) : IDisposable
{
    private readonly PackageHostKit _kit = new(factory);
    private readonly ContentTestHost _t = new(factory);

    public void Dispose()
    {
        _kit.Dispose();
        _t.Dispose();
    }

    private async Task<PackageUpload> PackageOfAsync(ContentWorld w)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        var file = new Lms.Domain.Files.StoredFile(classCourse.OrganizationId, classCourse.OrgUnitId, classCourse.OrgUnitPath, "p.zip", "application/zip", 8, DateTime.UtcNow);
        var upload = new PackageUpload(classCourse, Lesson1, PackageKinds.Scorm, "p.zip", file.Id, 8, new string('d', 64));
        upload.MarkReady("T", null, null, IPackageStore.PrefixOf(upload.OrganizationId, upload.Id));
        db.AddRange(file, upload);
        await db.SaveChangesAsync();
        await using var scope = _kit.Host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPackageStore>();
        await store.PutAsync(upload.StorePrefix, "index.html", "text/html; charset=utf-8", new MemoryStream("<p/>"u8.ToArray()), 4);
        return upload;
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string token) => (await client.GetAsync($"/p/{token}/index.html")).StatusCode;

    [Fact]
    public async Task Hidden_module_revokes_its_package_tokens()
    {
        var w = await _t.SetUpAsync("RH1");
        var package = await PackageOfAsync(w);
        var token = _kit.Token(package, w.StudentProfileId);
        var client = _kit.Content();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, token));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
            var lesson = await (from p in db.Set<ClassLessonPlan>() join l in db.Set<ClassLesson>() on p.Id equals l.PlanId
                                where p.ClassCourseId == classCourse.Id && l.Key == Lesson1 select l).FirstAsync();
            var module = new LessonModule(classCourse, lesson, ModuleKinds.Scorm, 1);
            module.SetRef(package.Id);

            await PackageRevocationHooks.ModuleChangedAsync(db, module, DateTime.UtcNow, CancellationToken.None); // visible: nothing
            Assert.False(db.ChangeTracker.Entries<PackageRevocation>().Any());

            module.Apply("ع", "T", ModuleVisibilities.Scheduled, DateTime.UtcNow.AddDays(1), null, ModuleTargets.ClassCourse, null, CompletionRules.None, null);
            await PackageRevocationHooks.ModuleChangedAsync(db, module, DateTime.UtcNow, CancellationToken.None); // hidden until tomorrow
            await db.SaveChangesAsync();
        }

        await Task.Delay(1100);
        await EventuallyAsync(async () => await GetAsync(client, token) == HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, _kit.Token(package, w.StudentProfileId))); // a re-launch after the marker works
    }

    [Fact]
    public async Task Unenrolled_student_revokes_their_package_tokens_and_leaves_others()
    {
        var w = await _t.SetUpAsync("RH2");
        var package = await PackageOfAsync(w);
        var token = _kit.Token(package, w.StudentProfileId);
        var bystander = _kit.Token(package, Guid.NewGuid());
        var client = _kit.Content();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, token));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var enrollment = await db.Set<CourseEnrollment>().FirstAsync(e => e.ClassCourseId == w.ClassCourse.Id && e.StudentId == w.StudentProfileId);
            await PackageRevocationHooks.EnrollmentsDroppedAsync(db, [enrollment], DateTime.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await EventuallyAsync(async () => await GetAsync(client, token) == HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, bystander));
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(500);
        }

        Assert.Fail("The condition did not hold within 15 s.");
    }
}
