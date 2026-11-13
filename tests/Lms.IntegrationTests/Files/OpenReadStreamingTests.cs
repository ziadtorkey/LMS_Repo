using System.Security.Cryptography;
using Lms.Domain.Files;
using Lms.Infrastructure;
using Lms.Infrastructure.Files;
using Lms.IntegrationTests.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lms.IntegrationTests.Files;

/// <summary>
/// <c>OpenReadAsync</c> must not hold a whole large object on the heap (#535, X-05): package archives can be 200 MB. Large objects
/// come back as a temp-file stream that deletes itself when disposed; small ones stay in memory.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpenReadStreamingTests(LmsApiFactory factory) : IDisposable
{
    private const int Mb = 1024 * 1024;
    private readonly string _spool = Path.Combine(Path.GetTempPath(), "lms-spool-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_spool))
        {
            Directory.Delete(_spool, recursive: true);
        }
    }

    private (MinioFileStorage Storage, Lms.Infrastructure.Persistence.LmsDbContext Db) Open(TestCurrentContext ctx, long inMemoryLimit)
    {
        var db = factory.CreateDbContext(ctx);
        var template = factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
        var options = Options.Create(new StorageOptions
        {
            Endpoint = template.Endpoint, PublicEndpoint = template.PublicEndpoint, AccessKey = template.AccessKey, SecretKey = template.SecretKey,
            Region = template.Region, AllowedExtensions = template.AllowedExtensions, InMemoryReadLimitBytes = inMemoryLimit, SpoolDirectory = _spool,
        });
        return (new MinioFileStorage(db, new Lms.Infrastructure.Configuration.SettingsResolver(db, ctx), new MinioClients(options), options, new SystemClock()), db);
    }

    private static byte[] Bytes(int length)
    {
        var data = new byte[length];
        new Random(535).NextBytes(data);
        return data;
    }

    [Fact]
    public async Task A_large_object_is_streamed_to_a_self_deleting_temp_file_with_the_same_bytes()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Stream1");
        var (storage, db) = Open(tree.AtRoot(), inMemoryLimit: 1 * Mb);
        await using var _db = db;
        var data = Bytes(6 * Mb);
        var stored = await storage.UploadAsync(new FileUploadRequest("pack.zip", "application/zip", data.Length, tree.School1.Id), new MemoryStream(data));

        var stream = await storage.OpenReadAsync(stored.Id);
        string path;
        await using (stream)
        {
            Assert.IsNotType<MemoryStream>(stream);
            var file = Assert.IsType<FileStream>(stream);
            path = file.Name;
            Assert.StartsWith(_spool, path);
            Assert.Equal(0, stream.Position);
            Assert.Equal(data.Length, stream.Length);
            Assert.Equal(SHA256.HashData(data), await SHA256.HashDataAsync(stream));
        }

        Assert.False(File.Exists(path), "the temp file must be gone once the stream is disposed");
    }

    [Fact]
    public async Task A_small_object_stays_in_memory_and_leaves_no_temp_file()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Stream2");
        var (storage, db) = Open(tree.AtRoot(), inMemoryLimit: 4 * Mb);
        await using var _db = db;
        var data = Bytes(64 * 1024);
        var stored = await storage.UploadAsync(new FileUploadRequest("small.zip", "application/zip", data.Length, tree.School1.Id), new MemoryStream(data));

        await using var stream = await storage.OpenReadAsync(stored.Id);

        Assert.IsType<MemoryStream>(stream);
        Assert.Equal(data, await ReadAllAsync(stream));
        Assert.False(Directory.Exists(_spool) && Directory.EnumerateFiles(_spool).Any());
    }

    [Fact]
    public async Task Cancelling_the_download_leaves_no_temp_file()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Stream3");
        var (storage, db) = Open(tree.AtRoot(), inMemoryLimit: 1 * Mb);
        await using var _db = db;
        var data = Bytes(6 * Mb);
        var stored = await storage.UploadAsync(new FileUploadRequest("pack.zip", "application/zip", data.Length, tree.School1.Id), new MemoryStream(data));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.OpenReadAsync(stored.Id, cts.Token));

        Assert.False(Directory.Exists(_spool) && Directory.EnumerateFiles(_spool).Any());
    }

    [Fact]
    public async Task Another_client_still_cannot_open_the_file()
    {
        var a = await ConfigurationTestTree.SeedAsync(factory, "Stream4A");
        var b = await ConfigurationTestTree.SeedAsync(factory, "Stream4B");
        var (storageA, dbA) = Open(a.AtRoot(), 1 * Mb);
        await using var _dbA = dbA;
        var data = Bytes(2 * Mb);
        var stored = await storageA.UploadAsync(new FileUploadRequest("pack.zip", "application/zip", data.Length, a.School1.Id), new MemoryStream(data));
        var (storageB, dbB) = Open(b.AtRoot(), 1 * Mb);
        await using var _dbB = dbB;

        await Assert.ThrowsAsync<StoredFileNotFoundException>(() => storageB.OpenReadAsync(stored.Id));
        Assert.False(Directory.Exists(_spool) && Directory.EnumerateFiles(_spool).Any());
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }
}
