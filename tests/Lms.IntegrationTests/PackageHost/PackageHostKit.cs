using System.Text;
using Lms.Api.PackageHost;
using Lms.Domain.Files;
using Lms.Domain.Organizations;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Packages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>An opened-store spy: records every prefix and path the host asks the package store for, then forwards to the real store.</summary>
internal sealed class SpyPackageStore(IPackageStore inner, List<(string Prefix, string Path)> opened) : IPackageStore
{
    public Task PutAsync(string prefix, string relativePath, string contentType, Stream content, long length, CancellationToken ct = default) =>
        inner.PutAsync(prefix, relativePath, contentType, content, length, ct);

    public Task<PackageFile?> OpenAsync(string prefix, string relativePath, CancellationToken ct = default)
    {
        lock (opened)
        {
            opened.Add((prefix, relativePath));
        }

        return inner.OpenAsync(prefix, relativePath, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) => inner.ListAsync(prefix, ct);

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default) => inner.DeletePrefixAsync(prefix, ct);
}

/// <summary>A host with the package content origin configured (the dev/CI pair of Decision 10: app on localhost, content on 127.0.0.1) and seeding helpers.</summary>
internal sealed class PackageHostKit : IDisposable
{
    public const string ContentOrigin = "http://127.0.0.1:5999";
    public const string AppOrigin = "http://localhost:5132";

    private readonly LmsApiFactory _factory;

    public PackageHostKit(
        LmsApiFactory factory, string? origin = ContentOrigin, string? appOrigin = AppOrigin, int cacheSeconds = 1, IPackagePageTransformer? transformer = null,
        IPackageHostAsset? asset = null)
    {
        _factory = factory;
        Host = factory.WithWebHostBuilder(builder =>
        {
            // The shared test factory configures a content origin for every host; an explicit empty value turns it off for this one.
            builder.UseSetting("PackageHost:Origin", origin ?? string.Empty);
            builder.UseSetting("PackageHost:AppOrigin", appOrigin ?? string.Empty);

            builder.UseSetting("PackageHost:RevocationCacheSeconds", cacheSeconds.ToString());
            builder.ConfigureServices(services =>
            {
                if (asset is not null)
                {
                    services.AddSingleton(asset);
                }

                if (transformer is not null)
                {
                    services.AddSingleton(transformer);
                }

                services.RemoveAll<IPackageStore>();
                services.AddScoped<IPackageStore>(sp =>
                    new SpyPackageStore(ActivatorUtilities.CreateInstance<Lms.Infrastructure.Packages.MinioPackageStore>(sp), Opened));
            });
        });
    }

    public WebApplicationFactory<Program> Host { get; }

    /// <summary>Every (prefix, path) the host asked the store for.</summary>
    public List<(string Prefix, string Path)> Opened { get; } = [];

    /// <summary>A client of the content host (requests carry the content origin's host and port).</summary>
    public HttpClient Content()
    {
        var client = Host.CreateClient();
        client.BaseAddress = new Uri(ContentOrigin);
        return client;
    }

    /// <summary>A client of the app (localhost).</summary>
    public HttpClient App() => Host.CreateClient();

    public PackageTokenService Tokens => Host.Services.GetRequiredService<PackageTokenService>();

    public string Token(PackageUpload package, Guid? student = null, bool preview = false, TimeSpan? lifetime = null, Guid? org = null, string? unit = null) =>
        Tokens.Issue(org ?? package.OrganizationId, unit ?? package.OrgUnitPath, package.Id, student, null, preview, lifetime ?? TimeSpan.FromMinutes(30), DateTimeOffset.UtcNow);

    /// <summary>A Ready package with the given files in the package store, under the unit.</summary>
    public async Task<PackageUpload> SeedPackageAsync(OrgUnit root, OrgUnit unit, params (string Path, string Text)[] files)
    {
        var year = await AcademicTestKit.AddYearAsync(_factory, root);
        var cls = await AcademicTestKit.AddClassAsync(_factory, unit, year);
        var course = await AcademicTestKit.AddCourseAsync(_factory, unit);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(_factory, cls, course);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var file = new StoredFile(classCourse.OrganizationId, classCourse.OrgUnitId, classCourse.OrgUnitPath, "package.zip", "application/zip", 8, DateTime.UtcNow);
        var upload = new PackageUpload(classCourse, Guid.NewGuid(), PackageKinds.Scorm, "course.zip", file.Id, 8, new string('c', 64));
        upload.MarkReady("Title", null, null, IPackageStore.PrefixOf(upload.OrganizationId, upload.Id));
        db.AddRange(file, upload);
        await db.SaveChangesAsync();

        await using var scope = Host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPackageStore>();
        foreach (var (path, text) in files)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await store.PutAsync(upload.StorePrefix, path, Lms.Infrastructure.Packages.PackageContentTypes.For(path).Type, new MemoryStream(bytes), bytes.Length);
        }

        return upload;
    }

    public void Dispose() => Host.Dispose();
}
