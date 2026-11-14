using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Blueprints;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Classes.LessonPlanTestKit;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Package upload, status, list, attach and delete (M4b Task 8a). Everything hangs off a class course at School1 with a two-lesson plan and
/// the ScormContent flag on for the client: School 2 and the other client get 404 (an invisible unit), a Teacher without an assignment to
/// the class course gets 404 (relationship). The upload body is JSON here (the matrix cannot send multipart), so a caller that passes
/// every check reaches the body check and answers 400; the attach route answers 409 package_not_ready for an upload that is not Ready.
/// </summary>
public sealed class PackageProbeRows : IProbeRowModule
{
    private static readonly Guid Section = Guid.NewGuid(), Lesson1 = Guid.NewGuid(), Lesson2 = Guid.NewGuid();

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4b });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("class course packages upload", HttpMethod.Post, Routes.ClassCoursePackagesPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCoursePackages((await NewClassCourseAsync(w)).Id), new { }),
            Expect(BadRequest, BadRequest, NotFound, NotFound, NotFound, BadRequest)),
        new("class course packages list", HttpMethod.Get, Routes.ClassCoursePackagesPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCoursePackages((await NewClassCourseAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("package get", HttpMethod.Get, Routes.PackagePattern,
            async (w, _) => new ProbeRequest(Routes.Package(await NewUploadAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("package delete", HttpMethod.Delete, Routes.PackagePattern,
            async (w, _) => new ProbeRequest(Routes.Package(await NewUploadAsync(w))),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
        new("package attach", HttpMethod.Post, Routes.PackageModulePattern,
            async (w, _) => new ProbeRequest(
                Routes.PackageModule(await NewUploadAsync(w)),
                new AttachPackageModuleRequest(new ModuleSettingsInput("حزمة", "Package", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null))),
            Expect(Conflict, Conflict, NotFound, NotFound, NotFound, Conflict)),
        // The grading method (D57): Modules.Manage on the class course; an upload that is not attached answers 409 package_not_attached.
        new("package grading method", HttpMethod.Put, Routes.PackageGradingMethodPattern,
            async (w, _) => new ProbeRequest(Routes.PackageGradingMethod(await NewUploadAsync(w)), new UpdatePackageGradingMethodRequest(PackageGradingMethods.Last)),
            Expect(Conflict, Conflict, NotFound, NotFound, NotFound, Conflict)),
    ];

    /// <summary>A class course with a two-lesson plan at School1; the ScormContent flag is on for the whole client.</summary>
    private static async Task<ClassCourse> NewClassCourseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(Section, "Week 1", new TestLesson(Lesson1, "Intro"), new TestLesson(Lesson2, "Basics")));
        await EnableFlagAsync(w);
        return await AddClassCourseWithPlanAsync(factory, cls, course);
    }

    private static async Task EnableFlagAsync(M1ProbeWorld w)
    {
        var a = w.Callers.A;
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        if (!await db.Set<OrgUnitSetting>().AnyAsync(s => s.OrganizationId == a.Org.Id && s.Key == FeatureFlags.ScormContent.Name))
        {
            db.Add(new OrgUnitSetting(a.Org.Id, a.Root.Id, a.Root.Path, FeatureFlags.ScormContent.Name, "true", false));
            await db.SaveChangesAsync();
        }
    }

    /// <summary>An upload in <c>Uploaded</c> written directly (the matrix has no multipart); the archive row points at a stored file.</summary>
    private static async Task<Guid> NewUploadAsync(M1ProbeWorld w)
    {
        var classCourse = await NewClassCourseAsync(w);
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id);
        var file = new Lms.Domain.Files.StoredFile(cc.OrganizationId, cc.OrgUnitId, cc.OrgUnitPath, "package.zip", "application/zip", 8, DateTime.UtcNow);
        var upload = new PackageUpload(cc, Lesson1, PackageKinds.Scorm, "course.zip", file.Id, 8, new string('a', 64));
        db.AddRange(file, upload);
        await db.SaveChangesAsync();
        return upload.Id;
    }
}
