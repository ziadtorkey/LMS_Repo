using System.IO.Compression;
using Lms.Api.Infrastructure;
using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Files;
using Lms.Domain.Packages;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Imports;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.Packages.Corpus;
using Lms.Shared.Configuration;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.Packages;

/// <summary>
/// M4b Task 8b: the validation job on the real database, file store and package store. The format validators of Tasks 10 and 11 are stood in
/// for by small fakes; what is under test is everything around them: the archive rules, quota, extraction, budgets, scanner hook, the store
/// and the clean-up.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PackageValidationJobTests(LmsApiFactory factory) : IDisposable
{
    private readonly List<ContentTestHost> _hosts = [];
    private readonly List<string> _tempRoots = [];

    public void Dispose()
    {
        _hosts.ForEach(h => h.Dispose());
        _tempRoots.Where(Directory.Exists).ToList().ForEach(d => Directory.Delete(d, recursive: true));
    }

    private sealed class FakeValidator(PackageValidationResult? result = null, bool hang = false) : IPackageValidator
    {
        public string Kind => PackageKinds.Scorm;

        public async Task<PackageValidationResult> ValidateAsync(IPackageContent content, CancellationToken ct)
        {
            if (hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return result ?? new PackageValidationResult(null, "Fake course", ScormVersions.Scorm12);
        }
    }

    private sealed class FakeScanner(bool clean) : IPackageScanner
    {
        public Task<bool> IsCleanAsync(IPackageContent content, CancellationToken ct) => Task.FromResult(clean);
    }

    private sealed class FailingStore(IPackageStore inner) : IPackageStore
    {
        public bool Fail { get; set; } = true;

        public Task PutAsync(string prefix, string relativePath, string contentType, Stream content, long length, CancellationToken ct = default) =>
            Fail && relativePath.StartsWith("index", StringComparison.Ordinal) ? throw new IOException("store down") : inner.PutAsync(prefix, relativePath, contentType, content, length, ct);

        public Task<PackageFile?> OpenAsync(string prefix, string relativePath, CancellationToken ct = default) => inner.OpenAsync(prefix, relativePath, ct);

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) => inner.ListAsync(prefix, ct);

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default) => inner.DeletePrefixAsync(prefix, ct);
    }

    private sealed class RecordingJobs : IPackageJobs
    {
        public List<(Guid Org, Guid Package, TimeSpan Delay)> Later { get; } = [];

        public void ScheduleValidation(Guid organizationId, Guid packageId)
        {
        }

        public void ScheduleValidationLater(Guid organizationId, Guid packageId, TimeSpan delay) => Later.Add((organizationId, packageId, delay));

        public void SchedulePurge(Guid organizationId, Guid packageId)
        {
        }
    }

    /// <summary>Soft-deletes the upload (as the delete route does) while its first file is being stored: the purge of the delete has already run.</summary>
    private sealed class DeletingStore(IPackageStore inner, Func<Task> deleteUpload) : IPackageStore
    {
        private bool _deleted;

        public async Task PutAsync(string prefix, string relativePath, string contentType, Stream content, long length, CancellationToken ct = default)
        {
            if (!_deleted)
            {
                _deleted = true;
                await deleteUpload();
            }

            await inner.PutAsync(prefix, relativePath, contentType, content, length, ct);
        }

        public Task<PackageFile?> OpenAsync(string prefix, string relativePath, CancellationToken ct = default) => inner.OpenAsync(prefix, relativePath, ct);

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) => inner.ListAsync(prefix, ct);

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default) => inner.DeletePrefixAsync(prefix, ct);
    }

    private sealed record Setup(ContentWorld World, OrgUnitTestHost Api, string TempRoot);

    private async Task<Setup> SetUpAsync(string name, Action<IServiceCollection>? configure = null, bool withValidator = true, TimeSpan? timeout = null)
    {
        var content = new ContentTestHost(factory);
        _hosts.Add(content);
        var world = await content.SetUpAsync(name);
        var tempRoot = Path.Combine(Path.GetTempPath(), "lms-job-test-" + Guid.NewGuid().ToString("N"));
        _tempRoots.Add(tempRoot);
        var api = new OrgUnitTestHost(factory, services =>
        {
            services.Configure<PackageValidationOptions>(o =>
            {
                o.TempRoot = tempRoot;
                o.Timeout = timeout ?? TimeSpan.FromMinutes(2);
            });
            services.RemoveAll<IPackageValidator>();
            if (withValidator)
            {
                services.AddScoped<IPackageValidator>(_ => new FakeValidator());
            }

            configure?.Invoke(services);
        });
        return new Setup(world, api, tempRoot);
    }

    private async Task<PackageUpload> UploadCorpusAsync(Setup s, string corpusName) =>
        await UploadAsync(s, await File.ReadAllBytesAsync(CorpusCases.CommittedFile(CorpusCases.All.Single(c => c.Name == corpusName))));

    private async Task<PackageUpload> UploadAsync(Setup s, byte[] bytes)
    {
        using var scope = s.Api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.NewGuid(), true);
        var db = scope.ServiceProvider.GetRequiredService<Lms.Infrastructure.Persistence.LmsDbContext>();
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == s.World.ClassCourse.Id);
        await using var stream = new MemoryStream(bytes);
        return await scope.ServiceProvider.GetRequiredService<PackageService>()
            .CreateAsync(classCourse, ContentTestHost.Lesson1, PackageKinds.Scorm, "course.zip", bytes.Length, stream, default);
    }

    private async Task RunAsync(Setup s, Guid organizationId, Guid packageId, CancellationToken ct = default)
    {
        using var scope = s.Api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.NewGuid(), true);
        await scope.ServiceProvider.GetRequiredService<PackageValidationJob>().RunAsync(organizationId, packageId, ct);
    }

    private async Task<PackageUpload> ReloadAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<PackageUpload>().AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task<IReadOnlyList<string>> StoredAsync(Setup s, PackageUpload upload)
    {
        using var scope = s.Api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPackageStore>().ListAsync(IPackageStore.PrefixOf(upload.OrganizationId, upload.Id));
    }

    private static int TempAreas(Setup s) => Directory.Exists(s.TempRoot) ? Directory.GetDirectories(s.TempRoot).Length : 0;

    private async Task SetAsync(ContentWorld w, string key, string json)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(w.Tree.Org.Id, w.Tree.Root.Id, w.Tree.Root.Path, key, json, false));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_valid_package_becomes_ready_with_its_files_in_the_store()
    {
        var s = await SetUpAsync("PV1");
        var upload = await UploadCorpusAsync(s, "scorm12_valid");

        await RunAsync(s, upload.OrganizationId, upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal((PackageUploadStatuses.Ready, "Fake course", ScormVersions.Scorm12), (row.Status, row.Title, row.Version));
        Assert.Equal(IPackageStore.PrefixOf(upload.OrganizationId, upload.Id), row.StorePrefix);
        Assert.True(row.ExtractedBytes > 0);
        Assert.Equal(["imsmanifest.xml", "index.html"], (await StoredAsync(s, row)).Order(StringComparer.Ordinal));
        using var scope = s.Api.Services.CreateScope();
        var html = (await scope.ServiceProvider.GetRequiredService<IPackageStore>().OpenAsync(row.StorePrefix, "index.html"))!;
        await using (html.Content)
        {
            Assert.Equal("text/html; charset=utf-8", html.ContentType);
        }

        Assert.Equal(0, TempAreas(s));
    }

    [Theory]
    [InlineData("zip_slip_dotdot", "unsafe_path")]
    [InlineData("symlink_entry", "symlink")]
    [InlineData("executable_php", "executable")]
    [InlineData("duplicate_case_fold", "duplicate_name")]
    [InlineData("not_a_zip_random_bytes", "invalid_archive")]
    [InlineData("bomb_ratio_one_entry", "compression_ratio")]
    public async Task An_archive_level_defect_rejects_the_upload_and_stores_nothing(string corpus, string reason)
    {
        var s = await SetUpAsync("PV2");
        var upload = await UploadCorpusAsync(s, corpus);

        await RunAsync(s, upload.OrganizationId, upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal((PackageUploadStatuses.Rejected, reason), (row.Status, row.RejectReason));
        Assert.Empty(await StoredAsync(s, row));
        Assert.Equal(0, TempAreas(s));
    }

    [Fact]
    public async Task The_registered_scorm_validator_decides_with_the_real_manifest()
    {
        var s = await SetUpAsync("PV3B", configure: services =>
        {
            services.RemoveAll<IPackageValidator>();
            services.AddScoped<IPackageValidator, Lms.Infrastructure.Packages.Scorm.ScormManifestValidator>();
        });
        var ok = await UploadCorpusAsync(s, "scorm12_valid");
        var multi = await UploadCorpusAsync(s, "multi_sco");
        var frames = await UploadCorpusAsync(s, "launch_frameset");

        await RunAsync(s, ok.OrganizationId, ok.Id);
        await RunAsync(s, multi.OrganizationId, multi.Id);
        await RunAsync(s, frames.OrganizationId, frames.Id);

        var ready = await ReloadAsync(ok.Id);
        Assert.Equal((PackageUploadStatuses.Ready, "SCO", ScormVersions.Scorm12), (ready.Status, ready.Title, ready.Version));
        Assert.Equal("multi_sco", (await ReloadAsync(multi.Id)).RejectReason);
        Assert.Equal("needs_nested_frames", (await ReloadAsync(frames.Id)).RejectReason);
        Assert.Empty(await StoredAsync(s, await ReloadAsync(multi.Id)));
    }

    [Fact]
    public async Task A_kind_without_a_validator_is_manifest_invalid()
    {
        var s = await SetUpAsync("PV3", withValidator: false);
        var upload = await UploadCorpusAsync(s, "scorm12_valid");

        await RunAsync(s, upload.OrganizationId, upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal(("Rejected", "manifest_invalid"), (row.Status, row.RejectReason));
        Assert.Empty(await StoredAsync(s, row));
    }

    [Fact]
    public async Task The_validators_reject_reason_is_kept_and_nothing_is_stored()
    {
        var s = await SetUpAsync("PV4", configure: services =>
        {
            services.RemoveAll<IPackageValidator>();
            services.AddScoped<IPackageValidator>(_ => new FakeValidator(PackageValidationResult.Reject("xml_unsafe")));
        });
        var upload = await UploadCorpusAsync(s, "scorm12_valid");

        await RunAsync(s, upload.OrganizationId, upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal(("Rejected", "xml_unsafe"), (row.Status, row.RejectReason));
        Assert.Empty(await StoredAsync(s, row));
    }

    [Fact]
    public async Task Scanner_rejection_marks_scan_failed()
    {
        var s = await SetUpAsync("PV5", configure: services =>
        {
            services.RemoveAll<IPackageScanner>();
            services.AddScoped<IPackageScanner>(_ => new FakeScanner(clean: false));
        });
        var upload = await UploadCorpusAsync(s, "scorm12_valid");

        await RunAsync(s, upload.OrganizationId, upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal(("Rejected", "scan_failed"), (row.Status, row.RejectReason));
        Assert.Empty(await StoredAsync(s, row));
    }

    [Fact]
    public async Task Quota_stops_a_package_that_would_exceed_the_client_storage()
    {
        var s = await SetUpAsync("PV6");
        await SetAsync(s.World, SettingKeys.PackagesMaxClientStorageMb.Name, "1");
        var random = new byte[1536 * 1024];
        new Random(7).NextBytes(random);
        var upload = await UploadAsync(s, ZipOf(("index.html", random)));

        await RunAsync(s, upload.OrganizationId, upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal(("Rejected", "quota_exceeded"), (row.Status, row.RejectReason));
        Assert.Empty(await StoredAsync(s, row));
        Assert.Equal(0, TempAreas(s));
    }

    [Fact]
    public async Task A_ready_package_counts_against_the_quota_and_a_deleted_one_frees_it()
    {
        var s = await SetUpAsync("PV7");
        await SetAsync(s.World, SettingKeys.PackagesMaxClientStorageMb.Name, "1");
        var half = new byte[700 * 1024];
        new Random(3).NextBytes(half);
        var first = await UploadAsync(s, ZipOf(("index.html", half)));
        await RunAsync(s, first.OrganizationId, first.Id);
        var second = await UploadAsync(s, ZipOf(("index.html", half)));

        await RunAsync(s, second.OrganizationId, second.Id);
        Assert.Equal("quota_exceeded", (await ReloadAsync(second.Id)).RejectReason);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<PackageUpload>().SingleAsync(p => p.Id == first.Id));
            await db.SaveChangesAsync();
        }

        var third = await UploadAsync(s, ZipOf(("index.html", half)));
        await RunAsync(s, third.OrganizationId, third.Id);
        Assert.Equal(PackageUploadStatuses.Ready, (await ReloadAsync(third.Id)).Status);
    }

    [Fact]
    public async Task Quota_and_job_timeout_stop_a_bomb()
    {
        var s = await SetUpAsync("PV8", configure: services =>
        {
            services.RemoveAll<IPackageValidator>();
            services.AddScoped<IPackageValidator>(_ => new FakeValidator(hang: true));
        }, timeout: TimeSpan.FromMilliseconds(400));
        var upload = await UploadCorpusAsync(s, "scorm12_valid");

        var started = DateTime.UtcNow;
        await RunAsync(s, upload.OrganizationId, upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal(("Rejected", "validation_timeout"), (row.Status, row.RejectReason));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
        Assert.Empty(await StoredAsync(s, row));
        Assert.Equal(0, TempAreas(s));
    }

    [Fact]
    public async Task A_busy_gate_reschedules_the_job_instead_of_holding_the_worker_and_other_clients_still_run()
    {
        var gate = new PackageValidationGate(Microsoft.Extensions.Options.Options.Create(new PackageValidationOptions { MaxConcurrent = 2 }));
        var jobs = new RecordingJobs();
        void Shared(IServiceCollection services)
        {
            services.RemoveAll<PackageValidationGate>();
            services.AddSingleton(gate);
            services.RemoveAll<IPackageJobs>();
            services.AddSingleton<IPackageJobs>(jobs);
        }

        var a = await SetUpAsync("PV13A", Shared);
        var b = await SetUpAsync("PV13B", Shared);
        var first = await UploadCorpusAsync(a, "scorm12_valid");
        var second = await UploadCorpusAsync(a, "scorm12_valid");
        var other = await UploadCorpusAsync(b, "scorm12_valid");
        await using var busy = gate.TryEnter(first.OrganizationId); // client A's validation is running

        var started = DateTime.UtcNow;
        await RunAsync(a, second.OrganizationId, second.Id); // returns at once: a worker is not held
        var waited = DateTime.UtcNow - started;
        await RunAsync(b, other.OrganizationId, other.Id); // another client is not blocked

        Assert.True(waited < TimeSpan.FromSeconds(2), $"the job waited {waited}");
        Assert.Equal(PackageUploadStatuses.Uploaded, (await ReloadAsync(second.Id)).Status);
        Assert.Equal([(second.OrganizationId, second.Id)], jobs.Later.Select(l => (l.Org, l.Package)));
        Assert.True(jobs.Later[0].Delay > TimeSpan.Zero);
        Assert.Equal(PackageUploadStatuses.Ready, (await ReloadAsync(other.Id)).Status);
    }

    [Fact]
    public async Task A_package_deleted_during_validation_leaves_nothing_in_the_store()
    {
        PackageUpload? target = null;
        var s = await SetUpAsync("PV14", configure: services =>
        {
            services.RemoveAll<IPackageStore>();
            services.AddScoped<IPackageStore>(sp => new DeletingStore(ActivatorUtilities.CreateInstance<MinioPackageStore>(sp), async () =>
            {
                await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
                db.Remove(await db.Set<PackageUpload>().SingleAsync(p => p.Id == target!.Id));
                await db.SaveChangesAsync();
            }));
        });
        target = await UploadCorpusAsync(s, "scorm12_valid");

        await RunAsync(s, target.OrganizationId, target.Id);

        Assert.Empty(await StoredAsync(s, target));
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await check.Set<PackageUpload>().IgnoreQueryFilters().SingleAsync(p => p.Id == target.Id);
        Assert.True(row.IsDeleted);
        Assert.NotEqual(PackageUploadStatuses.Ready, row.Status);
        Assert.Equal(0, TempAreas(s));
    }

    [Fact]
    public async Task Validation_job_cannot_read_another_clients_upload()
    {
        var s = await SetUpAsync("PV9");
        var upload = await UploadCorpusAsync(s, "scorm12_valid");

        await RunAsync(s, Guid.NewGuid(), upload.Id);

        var row = await ReloadAsync(upload.Id);
        Assert.Equal(PackageUploadStatuses.Uploaded, row.Status);
        Assert.Empty(await StoredAsync(s, row));
    }

    [Fact]
    public async Task A_replayed_job_changes_nothing_once_the_upload_is_decided()
    {
        var s = await SetUpAsync("PV10");
        var upload = await UploadCorpusAsync(s, "scorm12_valid");
        await RunAsync(s, upload.OrganizationId, upload.Id);
        var first = await ReloadAsync(upload.Id);

        await RunAsync(s, upload.OrganizationId, upload.Id);

        var second = await ReloadAsync(upload.Id);
        Assert.Equal((first.Status, first.ExtractedBytes, first.UpdatedAt), (second.Status, second.ExtractedBytes, second.UpdatedAt));
    }

    [Fact]
    public async Task A_store_failure_stores_nothing_and_leaves_the_upload_for_a_retry()
    {
        FailingStore? failing = null;
        var s = await SetUpAsync("PV11", configure: services =>
        {
            services.RemoveAll<IPackageStore>();
            services.AddScoped<IPackageStore>(sp => failing = new FailingStore(ActivatorUtilities.CreateInstance<MinioPackageStore>(sp)));
        });
        var upload = await UploadCorpusAsync(s, "scorm12_valid");

        await Assert.ThrowsAsync<IOException>(() => RunAsync(s, upload.OrganizationId, upload.Id));

        var row = await ReloadAsync(upload.Id);
        Assert.Equal(PackageUploadStatuses.Validating, row.Status);
        Assert.Empty(await StoredAsync(s, row));
        Assert.Equal(0, TempAreas(s));
        Assert.NotNull(failing);
    }

    [Fact]
    public async Task Upload_then_delete_runs_through_the_queued_jobs_and_purges_the_prefix()
    {
        var s = await SetUpAsync("PV12", services =>
        {
            services.RemoveAll<IBackgroundJobs>();
            services.AddScoped<IBackgroundJobs, RecordingBackgroundJobs>();
        });
        var upload = await UploadViaApiAsync(s);
        await RunQueuedAsync(s);
        var ready = await ReloadAsync(upload.Id);
        Assert.Equal(PackageUploadStatuses.Ready, ready.Status);
        Assert.NotEmpty(await StoredAsync(s, ready));

        var deleted = await s.Api.SendAsync(HttpMethod.Delete, Lms.Shared.Routes.Package(upload.Id), s.World.Teacher);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleted.StatusCode);
        await RunQueuedAsync(s);

        Assert.Empty(await StoredAsync(s, ready));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<StoredFile>().AnyAsync(f => f.Id == ready.ArchiveFileId));
    }

    private async Task<PackageUpload> UploadViaApiAsync(Setup s)
    {
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitSetting(s.World.Tree.Org.Id, s.World.Tree.Root.Id, s.World.Tree.Root.Path, FeatureFlags.ScormContent.Name, "true", false));
            await db.SaveChangesAsync();
        }

        var form = new MultipartFormDataContent
        {
            { new StringContent(ContentTestHost.Lesson1.ToString()), PackageUploadForm.LessonKey },
            { new StringContent(PackageKinds.Scorm), PackageUploadForm.Kind },
        };
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(CorpusCases.CommittedFile(CorpusCases.All.Single(c => c.Name == "scorm12_valid")))), PackageUploadForm.File, "course.zip");
        var request = new HttpRequestMessage(HttpMethod.Post, Lms.Shared.Routes.ClassCoursePackages(s.World.ClassCourse.Id)) { Content = form };
        var response = await s.Api.SendRawAsync(request, s.World.Teacher);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        var dto = (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<PackageUploadDto>(response.Content))!;
        return await ReloadAsync(dto.Id);
    }

    /// <summary>Runs the queued package jobs as the context that enqueued them; anything another test queued is put back.</summary>
    private static async Task RunQueuedAsync(Setup s)
    {
        var others = new List<(JobContextSnapshot Context, Delegate Job, Type Type)>();
        while (RecordingBackgroundJobs.Queue.TryDequeue(out var queued))
        {
            if (queued.Type != typeof(PackageValidationJob) && queued.Type != typeof(PackagePurgeJob))
            {
                others.Add(queued);
                continue;
            }

            using var scope = s.Api.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(queued.Context.UserId ?? Guid.Empty, queued.Context.IsPlatformAdmin);
            await (Task)queued.Job.DynamicInvoke(scope.ServiceProvider.GetRequiredService(queued.Type))!;
        }

        others.ForEach(RecordingBackgroundJobs.Queue.Enqueue);
    }

    private static byte[] ZipOf(params (string Name, byte[] Data)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in files)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                entry.Write(data);
            }
        }

        return ms.ToArray();
    }
}
