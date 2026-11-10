using System.Text;
using Lms.Domain.Files;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Files;
using Lms.Infrastructure;
using Lms.IntegrationTests.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lms.IntegrationTests.Files;

[Collection(ApiCollection.Name)]
public sealed class FileStorageTests(LmsApiFactory factory)
{
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 تقرير 🌟 sample bytes");

    private (MinioFileStorage Storage, Lms.Infrastructure.Persistence.LmsDbContext Db, SettingsResolver Settings) Open(TestCurrentContext ctx)
    {
        var db = factory.CreateDbContext(ctx);
        var settings = new SettingsResolver(db, ctx);
        var options = factory.Services.GetRequiredService<IOptions<StorageOptions>>();
        return (new MinioFileStorage(db, settings, new MinioClients(options), options, new SystemClock()), db, settings);
    }

    private static FileUploadRequest Request(Guid unitId, string name = "report.pdf", long? size = null) =>
        new(name, "application/pdf", size ?? Pdf.Length, unitId);

    [Fact]
    public async Task Upload_then_presigned_url_downloads_same_bytes()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "File1");
        var (storage, db, _) = Open(tree.AtRoot());
        await using var _db = db;

        var stored = await storage.UploadAsync(Request(tree.School1.Id, "تقرير.pdf"), new MemoryStream(Pdf));
        var url = await storage.GetDownloadUrlAsync(stored.Id);

        using var http = new HttpClient();
        Assert.Equal(Pdf, await http.GetByteArrayAsync(url));
        Assert.Contains($"lms-{tree.Org.Id:N}", url.AbsolutePath);
        Assert.StartsWith($"{tree.School1.Id:N}/", stored.ObjectKey);
        Assert.EndsWith(".pdf", stored.ObjectKey);
        Assert.Equal("تقرير.pdf", stored.FileName);
        Assert.Equal(Pdf.Length, stored.SizeBytes);
    }

    [Fact]
    public async Task Presigned_url_expires_after_its_ttl()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "File2");
        var (storage, db, _) = Open(tree.AtRoot());
        await using var _db = db;
        var stored = await storage.UploadAsync(Request(tree.School1.Id), new MemoryStream(Pdf));

        var url = await storage.GetDownloadUrlAsync(stored.Id, TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromSeconds(3));

        using var http = new HttpClient();
        Assert.False((await http.GetAsync(url)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Upload_over_max_size_is_rejected()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "File3");
        var (storage, db, settings) = Open(tree.AtRoot());
        await using var _db = db;
        await settings.SetAsync(SettingKeys.FilesMaxUploadMb, tree.Root.Id, 1, isLocked: false);

        var ex = await Assert.ThrowsAsync<FileTooLargeException>(
            () => storage.UploadAsync(Request(tree.School1.Id, size: 2L * 1024 * 1024), new MemoryStream(Pdf)));

        Assert.Equal(1024 * 1024, ex.MaxBytes);
        Assert.Empty(await db.Set<StoredFile>().ToListAsync());
    }

    [Fact]
    public async Task Disallowed_extension_is_rejected()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "File4");
        var (storage, db, _) = Open(tree.AtRoot());
        await using var _db = db;

        await Assert.ThrowsAsync<FileTypeNotAllowedException>(
            () => storage.UploadAsync(Request(tree.School1.Id, "malware.exe"), new MemoryStream(Pdf)));
        await Assert.ThrowsAsync<FileTypeNotAllowedException>(
            () => storage.UploadAsync(Request(tree.School1.Id, "noextension"), new MemoryStream(Pdf)));
    }

    [Fact]
    public async Task Other_client_cannot_get_url_for_file()
    {
        var a = await ConfigurationTestTree.SeedAsync(factory, "File5A");
        var b = await ConfigurationTestTree.SeedAsync(factory, "File5B");
        var (storageA, dbA, _) = Open(a.AtRoot());
        await using var _dbA = dbA;
        var stored = await storageA.UploadAsync(Request(a.School1.Id), new MemoryStream(Pdf));

        var (storageB, dbB, _) = Open(b.AtRoot());
        await using var _dbB = dbB;

        await Assert.ThrowsAsync<StoredFileNotFoundException>(() => storageB.GetDownloadUrlAsync(stored.Id));
        await Assert.ThrowsAsync<StoredFileNotFoundException>(() => storageB.DeleteAsync(stored.Id));
    }

    [Fact]
    public async Task Sibling_unit_scope_cannot_get_url_for_file()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "File6");
        var (storage, db, _) = Open(tree.AtRoot());
        await using var _db = db;
        var stored = await storage.UploadAsync(Request(tree.School1.Id), new MemoryStream(Pdf));

        var (siblingStorage, siblingDb, _) = Open(TestCurrentContext.For(tree.Org.Id, tree.School2.Path));
        await using var _siblingDb = siblingDb;

        await Assert.ThrowsAsync<StoredFileNotFoundException>(() => siblingStorage.GetDownloadUrlAsync(stored.Id));
    }

    [Fact]
    public async Task Upload_into_a_unit_outside_the_scope_is_rejected()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "File7");
        var (storage, db, _) = Open(TestCurrentContext.For(tree.Org.Id, tree.School1.Path));
        await using var _db = db;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.UploadAsync(Request(tree.School2.Id), new MemoryStream(Pdf)));
    }

    [Fact]
    public async Task Deleted_file_has_no_download_url_but_keeps_its_record()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "File8");
        var (storage, db, _) = Open(tree.AtRoot());
        await using var _db = db;
        var stored = await storage.UploadAsync(Request(tree.School1.Id), new MemoryStream(Pdf));

        await storage.DeleteAsync(stored.Id);

        await Assert.ThrowsAsync<StoredFileNotFoundException>(() => storage.GetDownloadUrlAsync(stored.Id));
        var row = await db.Set<StoredFile>().IgnoreQueryFilters().SingleAsync(x => x.Id == stored.Id);
        Assert.True(row.IsDeleted);
    }

    [Fact]
    public async Task Files_are_isolated_by_subtree_and_client_at_the_table_level()
    {
        var a = await ConfigurationTestTree.SeedAsync(factory, "File9A");
        var b = await ConfigurationTestTree.SeedAsync(factory, "File9B");
        var (storage, db, _) = Open(a.AtRoot());
        await using var _db = db;
        await storage.UploadAsync(Request(a.School1.Id), new MemoryStream(Pdf));
        await storage.UploadAsync(Request(a.School2.Id), new MemoryStream(Pdf));

        await using var asSchool1 = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));
        Assert.Equal([a.School1.Id], await asSchool1.Set<StoredFile>().Select(x => x.OrgUnitId).ToListAsync());
        await using var asB = factory.CreateDbContext(b.AtRoot());
        Assert.Empty(await asB.Set<StoredFile>().ToListAsync());
        await using var noScope = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, null));
        Assert.Empty(await noScope.Set<StoredFile>().ToListAsync());
    }
}
