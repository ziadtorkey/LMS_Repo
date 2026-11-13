using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
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
/// Launch, preview, my-state, grading method, commit and finish of a package (M4b Task 9b). Everything hangs off an attached, ready
/// package of a class course at School1. Launch, my-state, commit and finish belong to a student (the probe callers are staff): staff get
/// 404, never data. Preview and the grading method follow the usual rule: School 2 and the other client get 404 (an invisible unit), a
/// Teacher without an assignment to the class course gets 404 (relationship).
/// </summary>
public sealed class PackageAttemptProbeRows : IProbeRowModule
{
    private static readonly Guid Section = Guid.NewGuid(), Lesson1 = Guid.NewGuid();

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4b });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("package launch", HttpMethod.Post, Routes.PackageLaunchPattern,
            async (w, _) => new ProbeRequest(Routes.PackageLaunch((await NewAttachedAsync(w)).Package)),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)),
        new("package preview", HttpMethod.Post, Routes.PackagePreviewPattern,
            async (w, _) => new ProbeRequest(Routes.PackagePreview((await NewAttachedAsync(w)).Package)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("package my-state", HttpMethod.Get, Routes.PackageMyStatePattern,
            async (w, _) => new ProbeRequest(Routes.PackageMyState((await NewAttachedAsync(w)).Package)),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)),
        new("package grading method", HttpMethod.Put, Routes.PackageGradingMethodPattern,
            async (w, _) => new ProbeRequest(Routes.PackageGradingMethod((await NewAttachedAsync(w)).Package), new UpdatePackageGradingMethodRequest(PackageGradingMethods.Last)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("package attempt commit", HttpMethod.Post, Routes.PackageAttemptCommitPattern,
            async (w, _) => new ProbeRequest(Routes.PackageAttemptCommit(await NewAttemptAsync(w)),
                new PackageCommitRequest(0, PackageCommitFormats.Scorm, new Dictionary<string, string> { ["cmi.suspend_data"] = "x" })),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)),
        new("package attempt finish", HttpMethod.Post, Routes.PackageAttemptFinishPattern,
            async (w, _) => new ProbeRequest(Routes.PackageAttemptFinish(await NewAttemptAsync(w)), new PackageFinishRequest(0)),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)),
    ];

    /// <summary>A class course with a one-lesson plan at School1; the ScormContent flag is on for the whole client.</summary>
    private static async Task<ClassCourse> NewClassCourseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, new TestSection(Section, "Week 1", new TestLesson(Lesson1, "Intro")));
        var a = w.Callers.A;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            if (!await db.Set<OrgUnitSetting>().AnyAsync(s => s.OrganizationId == a.Org.Id && s.Key == FeatureFlags.ScormContent.Name))
            {
                db.Add(new OrgUnitSetting(a.Org.Id, a.Root.Id, a.Root.Path, FeatureFlags.ScormContent.Name, "true", false));
                await db.SaveChangesAsync();
            }
        }

        return await AddClassCourseWithPlanAsync(factory, cls, course);
    }

    /// <summary>A ready package attached to the lesson through the attach route (as the root admin), so the module and the grading method are real.</summary>
    private static async Task<(Guid Package, Guid Module)> NewAttachedAsync(M1ProbeWorld w)
    {
        var classCourse = await NewClassCourseAsync(w);
        Guid packageId;
        await using (var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id);
            var file = new Lms.Domain.Files.StoredFile(cc.OrganizationId, cc.OrgUnitId, cc.OrgUnitPath, "package.zip", "application/zip", 8, DateTime.UtcNow);
            var upload = new PackageUpload(cc, Lesson1, PackageKinds.Scorm, "course.zip", file.Id, 8, new string('a', 64));
            upload.MarkReady("Package", ScormVersions.Scorm12, null, $"{cc.OrganizationId}/{upload.Id}/");
            db.AddRange(file, upload);
            await db.SaveChangesAsync();
            packageId = upload.Id;
        }

        var module = new ModuleSettingsInput("حزمة", "Package", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Submit, null);
        var node = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.PackageModule(packageId), new AttachPackageModuleRequest(module));
        return (packageId, node.GetProperty("id").GetGuid());
    }

    /// <summary>An open attempt of a student of School1, written directly (the probe callers are staff).</summary>
    private static async Task<Guid> NewAttemptAsync(M1ProbeWorld w)
    {
        var (packageId, moduleId) = await NewAttachedAsync(w);
        var student = await PeopleTestKit.AddStudentAsync(w.Callers.Factory, w.Callers.A.School1, PeopleTestKit.NewCode());
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var package = await db.Set<PackageUpload>().SingleAsync(p => p.Id == packageId);
        var attempt = new PackageAttempt(package, moduleId, student.Id, PackageCommitFormats.Scorm, DateTime.UtcNow);
        db.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }
}
