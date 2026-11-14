using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.UnitTests.Packages;

/// <summary>The purge of the extracted files of a deleted package (M4b Task 8b).</summary>
public sealed class PackagePurgeJobTests
{
    private sealed class FakeStore : IPackageStore
    {
        public int FailuresLeft { get; set; }

        public List<string> Deleted { get; } = [];

        public Task PutAsync(string prefix, string relativePath, string contentType, Stream content, long length, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<PackageFile?> OpenAsync(string prefix, string relativePath, CancellationToken ct = default) => Task.FromResult<PackageFile?>(null);

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default)
        {
            if (FailuresLeft-- > 0)
            {
                throw new IOException("store down");
            }

            Deleted.Add(prefix);
            return Task.FromResult(3);
        }
    }

    [Fact]
    public async Task The_prefix_comes_from_the_two_ids_of_the_payload()
    {
        var store = new FakeStore();
        var org = Guid.NewGuid();
        var package = Guid.NewGuid();

        await new PackagePurgeJob(store, NullLogger<PackagePurgeJob>.Instance).RunAsync(org, package, default);

        Assert.Equal([IPackageStore.PrefixOf(org, package)], store.Deleted);
        Assert.Equal($"{org:N}/{package:N}/", store.Deleted[0]);
    }

    [Fact]
    public async Task Purge_retries_after_a_store_failure()
    {
        var store = new FakeStore { FailuresLeft = 1 };
        var job = new PackagePurgeJob(store, NullLogger<PackagePurgeJob>.Instance);
        var org = Guid.NewGuid();
        var package = Guid.NewGuid();

        await Assert.ThrowsAsync<IOException>(() => job.RunAsync(org, package, default));
        Assert.Empty(store.Deleted);

        await job.RunAsync(org, package, default); // the retry Hangfire makes

        Assert.Equal([IPackageStore.PrefixOf(org, package)], store.Deleted);
    }
}
