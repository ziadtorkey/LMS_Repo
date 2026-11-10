using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Lms.Infrastructure.Files;

/// <summary>Singleton holder of the MinIO clients: one for operations, one that signs urls for the public endpoint.</summary>
public sealed class MinioClients
{
    private readonly ConcurrentDictionary<string, bool> _knownBuckets = new();

    public MinioClients(IOptions<StorageOptions> options)
    {
        var o = options.Value;
        Client = Build(o.Endpoint, o);
        // Signing needs no network call when the region is set, so the public host can differ from the internal one.
        Presigner = string.IsNullOrWhiteSpace(o.PublicEndpoint) ? Client : Build(o.PublicEndpoint, o);
    }

    public IMinioClient Client { get; }
    public IMinioClient Presigner { get; }

    public async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        if (_knownBuckets.ContainsKey(bucket))
        {
            return;
        }

        if (!await Client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), cancellationToken))
        {
            try
            {
                await Client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), cancellationToken);
            }
            catch (Exception)
            {
                // A lost creation race with another request is fine: the bucket exists, which is all that is needed.
                if (!await Client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), cancellationToken))
                {
                    throw;
                }
            }
        }

        _knownBuckets[bucket] = true;
    }

    private static IMinioClient Build(string endpoint, StorageOptions o)
    {
        var withScheme = endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : "http://" + endpoint;
        var uri = new Uri(withScheme);
        var client = new MinioClient()
            .WithEndpoint(uri.Host, uri.Port)
            .WithCredentials(o.AccessKey, o.SecretKey)
            .WithRegion(o.Region);
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            client = client.WithSSL();
        }

        return client.Build();
    }
}
