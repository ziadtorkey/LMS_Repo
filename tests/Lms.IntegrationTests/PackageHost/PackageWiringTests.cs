using System.Net;
using Lms.Domain.Enrollment;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.Enrollment;
using Lms.Shared.Packages;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using Serilog.Events;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>
/// End to end through the real handlers (#596): hiding the module through the module endpoint and dropping the enrollment through the
/// enrollment endpoint make the content host answer 404 for tokens already issued; and real host requests never write a token to a log.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PackageWiringTests : IDisposable
{
    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events)
            {
                Events.Add(logEvent);
            }
        }
    }

    private readonly LmsApiFactory _factory;
    private readonly CollectingSink _sink = new();
    private readonly PackageHostKit _kit;
    private readonly ContentTestHost _t;

    public PackageWiringTests(LmsApiFactory factory)
    {
        _factory = factory;
        _kit = new PackageHostKit(factory, logSink: _sink, platformAdminScopes: true);
        _t = new ContentTestHost(factory);
    }

    public void Dispose()
    {
        _kit.Dispose();
        _t.Dispose();
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string token) => (await client.GetAsync($"/p/{token}/index.html")).StatusCode;

    private static async Task EventuallyNotFoundAsync(HttpClient client, string token)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await GetAsync(client, token) == HttpStatusCode.NotFound)
            {
                return;
            }

            await Task.Delay(500);
        }

        Assert.Fail("The token still worked after 15 s.");
    }

    private async Task<(PackageUpload Package, Guid ModuleId, ContentWorld World)> LiveAsync(string name)
    {
        var w = await _t.SetUpAsync(name);
        // A package of the world's class course, attached to a visible SCORM module in its lesson plan.
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        var file = new Lms.Domain.Files.StoredFile(classCourse.OrganizationId, classCourse.OrgUnitId, classCourse.OrgUnitPath, "p.zip", "application/zip", 8, DateTime.UtcNow);
        var upload = new PackageUpload(classCourse, Lesson1, PackageKinds.Scorm, "p.zip", file.Id, 8, new string('e', 64));
        upload.MarkReady("T", null, null, IPackageStore.PrefixOf(upload.OrganizationId, upload.Id));
        db.AddRange(file, upload);
        await db.SaveChangesAsync();
        await using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(_kit.Host.Services))
        {
            var store = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IPackageStore>(scope.ServiceProvider);
            await store.PutAsync(upload.StorePrefix, "index.html", "text/html; charset=utf-8", new MemoryStream("<p/>"u8.ToArray()), 4);
        }

        return (upload, await _kit.AttachModuleAsync(upload), w);
    }

    [Fact]
    public async Task Hiding_the_module_through_the_module_writer_makes_the_host_answer_404()
    {
        var (package, moduleId, w) = await LiveAsync("PW1");
        var token = _kit.Token(package, w.StudentProfileId);
        var client = _kit.Content();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, token));

        // No HTTP route edits a SCORM/H5P module yet (the module update endpoint takes content kinds only: 400), so the real seam is IModuleWriter,
        // the one feature kinds use for their modules; the hook lives in it.
        await using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(_kit.Host.Services))
        {
            var writer = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Lms.Domain.Content.IModuleWriter>(scope.ServiceProvider);
            await writer.UpdateMetadataAsync(moduleId, new Lms.Domain.Content.ModuleMetadata(
                "ع", "T", ModuleVisibilities.Scheduled, DateTime.UtcNow.AddDays(1), null, ModuleTargets.ClassCourse, null, CompletionRules.None, null));
        }

        await EventuallyNotFoundAsync(client, token);
    }

    [Fact]
    public async Task Dropping_the_enrollment_through_the_endpoint_makes_the_host_answer_404_for_that_student_only()
    {
        var (package, _, w) = await LiveAsync("PW2");
        var token = _kit.Token(package, w.StudentProfileId);
        var bystander = _kit.Token(package, Guid.NewGuid());
        var client = _kit.Content();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, token));
        Guid enrollmentId;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            enrollmentId = (await db.Set<CourseEnrollment>().FirstAsync(e => e.ClassCourseId == w.ClassCourse.Id && e.StudentId == w.StudentProfileId)).Id;
        }

        var drop = await _t.SendAsync(w.Admin, HttpMethod.Post, Routes.EnrollmentDrop(enrollmentId), new DropEnrollmentRequest("left", null));

        Assert.Equal(HttpStatusCode.NoContent, drop.StatusCode);
        await EventuallyNotFoundAsync(client, token);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, bystander));
    }

    [Fact]
    public async Task Real_host_requests_never_write_the_token_to_a_log()
    {
        var (package, _, w) = await LiveAsync("PW3");
        var token = _kit.Token(package, w.StudentProfileId);
        var forged = token[..^6] + "AAAAAA";
        var client = _kit.Content();

        await client.GetAsync($"/p/{token}/index.html");
        await client.GetAsync($"/p/{token}/missing/a%2Fb.js?x=1");
        await client.GetAsync($"/p/{forged}/index.html");
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/p/{token}/index.html"));
        await client.GetAsync($"/p/{Uri.EscapeDataString(token)}/index.html");

        LogEvent[] events;
        lock (_sink.Events)
        {
            events = [.. _sink.Events];
        }

        // The capture must be real: the hosting diagnostics wrote request lines for the content host's paths.
        Assert.True(events.Any(e => e.RenderMessage().Contains("/p/[redacted]/", StringComparison.Ordinal)), $"{events.Length} events; sample: {string.Join(" | ", events.Where(e => e.RenderMessage().Contains("/p/")).Take(6).Select(e => e.RenderMessage()))}");
        var fragments = new[] { token, forged, Uri.EscapeDataString(token), token[..40], token[^40..] };
        foreach (var e in events)
        {
            var text = e.RenderMessage() + " " + string.Join(" ", e.Properties.Values.Select(v => v.ToString())) + " " + e.Exception;
            Assert.All(fragments, f => Assert.True(!text.Contains(f, StringComparison.Ordinal), "LEAK in [" + e.Level + "] " + e.MessageTemplate.Text + " => " + text.Replace(f, "<FRAGMENT>")[..Math.Min(400, text.Length)]));
        }
    }
}
