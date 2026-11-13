using Lms.Domain.Packages;
using Lms.Infrastructure.Files;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// <see cref="IPackageStore"/> on the platform's MinIO: one bucket for all clients, the client and the package are the first two key
/// segments (<c>{organizationId}/{packageId}/path</c>), so a package is addressed by ids the API already checked, never by a path.
/// </summary>
public sealed class MinioPackageStore(MinioClients clients) : IPackageStore
{
    public const string Bucket = "lms-packages";

    public async Task PutAsync(string prefix, string relativePath, string contentType, Stream content, long length, CancellationToken ct = default)
    {
        EnsureSafe(prefix, relativePath);
        await clients.EnsureBucketAsync(Bucket, ct);
        await clients.Client.PutObjectAsync(
            new PutObjectArgs().WithBucket(Bucket).WithObject(prefix + relativePath).WithStreamData(content).WithObjectSize(length)
                .WithContentType(contentType),
            ct);
    }

    public async Task<PackageFile?> OpenAsync(string prefix, string relativePath, CancellationToken ct = default)
    {
        EnsureSafe(prefix, relativePath);
        await clients.EnsureBucketAsync(Bucket, ct);
        var key = prefix + relativePath;
        try
        {
            var stat = await clients.Client.StatObjectAsync(new StatObjectArgs().WithBucket(Bucket).WithObject(key), ct);
            // A temp file that goes away with the stream: a large video must not sit in memory.
            var temp = new FileStream(
                Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            await clients.Client.GetObjectAsync(
                new GetObjectArgs().WithBucket(Bucket).WithObject(key).WithCallbackStream(stream => stream.CopyTo(temp)), ct);
            temp.Position = 0;
            return new PackageFile(temp, stat.ContentType, stat.Size);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default)
    {
        await clients.EnsureBucketAsync(Bucket, ct);
        var keys = new List<string>();
        await foreach (var item in clients.Client.ListObjectsEnumAsync(
                           new ListObjectsArgs().WithBucket(Bucket).WithPrefix(prefix).WithRecursive(true), ct))
        {
            keys.Add(item.Key[prefix.Length..]);
        }

        return keys;
    }

    public async Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        var relative = await ListAsync(prefix, ct);
        if (relative.Count == 0)
        {
            return 0;
        }

        var results = await clients.Client.RemoveObjectsAsync(
            new RemoveObjectsArgs().WithBucket(Bucket).WithObjects(relative.Select(r => prefix + r).ToList()), ct);
        _ = results;
        return relative.Count;
    }

    /// <summary>
    /// Defence in depth: the hardening reader never lets a bad path through, but the store does not trust its caller either. A relative path may
    /// not climb (<c>..</c>), be rooted, use a backslash or contain a control character, and the prefix must be a <c>{org}/{package}/</c> pair.
    /// </summary>
    private static void EnsureSafe(string prefix, string relativePath)
    {
        var bad = relativePath.Length == 0 || relativePath.StartsWith('/') || relativePath.Contains('\\') || relativePath.Any(char.IsControl)
                  || relativePath.Split('/').Any(segment => segment is ".." or "." or "") || prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2;
        if (bad)
        {
            throw new ArgumentException("An unsafe package store path.", nameof(relativePath));
        }
    }
}
