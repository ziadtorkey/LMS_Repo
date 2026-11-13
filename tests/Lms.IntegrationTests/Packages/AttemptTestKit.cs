using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Files;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Packages;

/// <summary>The format adapter of the tests: a SCORM 1.2 lesson_status of passed or completed completes, the score comes from cmi.core.score.*.</summary>
internal sealed class TestScormTracker : IPackageTracker
{
    public string Kind => PackageKinds.Scorm;

    public string LaunchPath(PackageUpload package) => "story.html";

    public TrackingJudgement Judge(PackageUpload package, string completionRule, decimal? minScore, IReadOnlyDictionary<string, string> model)
    {
        decimal? Num(string key) => model.TryGetValue(key, out var v) && decimal.TryParse(v, CultureInfo.InvariantCulture, out var d) ? d : null;
        var raw = Num("cmi.core.score.raw");
        var done = model.GetValueOrDefault("cmi.core.lesson_status") is "passed" or "completed";
        if (done && completionRule == CompletionRules.Score && minScore is { } min && (raw ?? 0) < min)
        {
            done = false;
        }

        return new TrackingJudgement(done, raw, Num("cmi.core.score.min"), Num("cmi.core.score.max"));
    }
}

/// <summary>A package host, a ready and attached package, and calls for a student's attempts.</summary>
internal sealed class AttemptKit : IDisposable
{
    public const string ContentOrigin = "http://127.0.0.1:5999";

    private readonly LmsApiFactory _factory;

    public AttemptKit(LmsApiFactory factory, string? origin = ContentOrigin)
    {
        _factory = factory;
        Host = new ContentTestHost(factory, services =>
        {
            services.Configure<PackageHostOptions>(o => o.Origin = origin);
            services.AddSingleton<IPackageTracker, TestScormTracker>();
        });
    }

    public ContentTestHost Host { get; }

    public void Dispose() => Host.Dispose();

    /// <summary>A world with the feature on and no commit rate limit (a test that needs the limit sets it itself).</summary>
    public async Task<ContentWorld> SetUpAsync(string name, bool flag = true, int commitIntervalSeconds = 0)
    {
        var w = await Host.SetUpAsync(name);
        if (flag)
        {
            await SetAsync(w, FeatureFlags.ScormContent.Name, "true");
        }

        await SetAsync(w, SettingKeys.PackagesCommitMinIntervalSeconds.Name, commitIntervalSeconds.ToString(CultureInfo.InvariantCulture));
        return w;
    }

    public async Task SetAsync(ContentWorld w, string key, string json)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(w.Tree.Org.Id, w.Tree.Root.Id, w.Tree.Root.Path, key, json, false));
        await db.SaveChangesAsync();
    }

    /// <summary>A validated package of the class course, not yet attached to a lesson.</summary>
    public async Task<PackageUpload> SeedReadyAsync(ContentWorld w, string kind = PackageKinds.Scorm)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        var file = new StoredFile(cc.OrganizationId, cc.OrgUnitId, cc.OrgUnitPath, "p.zip", "application/zip", 10, DateTime.UtcNow);
        var upload = new PackageUpload(cc, Lesson1, kind, "p.zip", file.Id, 10, new string('a', 64));
        upload.MarkReady("Package", ScormVersions.Scorm12, null, $"{cc.OrganizationId}/{upload.Id}/");
        db.AddRange(file, upload);
        await db.SaveChangesAsync();
        return upload;
    }

    /// <summary>A package attached to the lesson by the teacher (the attach route, so the grading method and the module follow the real path).</summary>
    public async Task<(PackageUpload Package, ModuleNodeDto Module)> AttachedAsync(
        ContentWorld w, string method = PackageGradingMethods.Highest, string rule = CompletionRules.Submit, decimal? minScore = null,
        string visibility = ModuleVisibilities.Published)
    {
        var upload = await SeedReadyAsync(w);
        var module = new ModuleSettingsInput("حزمة", "Package", visibility, null, null, ModuleTargets.ClassCourse, null, rule, minScore);
        var response = await Host.SendAsync(w.Teacher, HttpMethod.Post, Routes.PackageModule(upload.Id), new AttachPackageModuleRequest(module, method));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (upload, (await response.Content.ReadFromJsonAsync<ModuleNodeDto>())!);
    }

    public async Task<PackageLaunchDto> LaunchAsync(RoleAssignment who, Guid packageId)
    {
        var response = await Host.SendAsync(who, HttpMethod.Post, Routes.PackageLaunch(packageId));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"launch: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<PackageLaunchDto>())!;
    }

    public Task<HttpResponseMessage> CommitAsync(RoleAssignment who, Guid attemptId, long seq, params (string Key, string Value)[] data) =>
        Host.SendAsync(who, HttpMethod.Post, Routes.PackageAttemptCommit(attemptId),
            new PackageCommitRequest(seq, PackageCommitFormats.Scorm, data.ToDictionary(d => d.Key, d => d.Value)));

    public Task<HttpResponseMessage> FinishAsync(RoleAssignment who, Guid attemptId, long lastSeq) =>
        Host.SendAsync(who, HttpMethod.Post, Routes.PackageAttemptFinish(attemptId), new PackageFinishRequest(lastSeq));

    /// <summary>Launch, report a completed lesson with the score, finish: one graded attempt.</summary>
    public async Task<PackageAttemptDto> CompleteAsync(RoleAssignment student, Guid packageId, decimal score)
    {
        var launch = await LaunchAsync(student, packageId);
        var commit = await CommitAsync(student, launch.AttemptId, 0,
            ("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", score.ToString(CultureInfo.InvariantCulture)), ("cmi.core.score.max", "100"));
        Assert.Equal(HttpStatusCode.NoContent, commit.StatusCode);
        var finish = await FinishAsync(student, launch.AttemptId, 0);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        return (await finish.Content.ReadFromJsonAsync<PackageAttemptDto>())!;
    }

    public async Task<List<Lms.Domain.Grading.GradeEvent>> GradeEventsAsync(Guid packageId)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Lms.Domain.Grading.GradeEvent>().Where(e => e.SourceId == packageId).OrderBy(e => e.Revision).ToListAsync();
    }
}
