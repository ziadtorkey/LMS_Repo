using System.Text;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Packages;

/// <summary>The package store on the real MinIO: a prefix per client and package, content types as stored, listing and purge of one prefix only (M4b Task 8a).</summary>
[Collection(ApiCollection.Name)]
public sealed class PackageStoreTests(LmsApiFactory factory)
{
    private static async Task PutAsync(IPackageStore store, string prefix, string path, string text, string type = "text/plain")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await store.PutAsync(prefix, path, type, new MemoryStream(bytes), bytes.Length);
    }

    private IPackageStore Store(out IServiceScope scope)
    {
        scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPackageStore>();
    }

    [Fact]
    public async Task Files_round_trip_under_a_prefix_with_their_stored_type()
    {
        var store = Store(out var scope);
        using var disposable = scope;
        var prefix = IPackageStore.PrefixOf(Guid.NewGuid(), Guid.NewGuid());

        await PutAsync(store, prefix, "index.html", "<p>hi</p>", "text/html; charset=utf-8");
        await PutAsync(store, prefix, "js/app.js", "x", "text/javascript; charset=utf-8");

        var html = (await store.OpenAsync(prefix, "index.html"))!;
        await using (html.Content)
        {
            using var reader = new StreamReader(html.Content);
            Assert.Equal("<p>hi</p>", await reader.ReadToEndAsync());
        }

        var js = (await store.OpenAsync(prefix, "js/app.js"))!;
        await using (js.Content)
        {
            Assert.Equal(("text/javascript; charset=utf-8", 1L), (js.ContentType, js.Length));
        }

        Assert.Equal(["index.html", "js/app.js"], (await store.ListAsync(prefix)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_missing_file_is_null_and_another_package_never_answers()
    {
        var store = Store(out var scope);
        using var disposable = scope;
        var org = Guid.NewGuid();
        var mine = IPackageStore.PrefixOf(org, Guid.NewGuid());
        var other = IPackageStore.PrefixOf(org, Guid.NewGuid());
        await PutAsync(store, mine, "index.html", "mine");

        Assert.Null(await store.OpenAsync(mine, "missing.html"));
        Assert.Null(await store.OpenAsync(other, "index.html"));
        Assert.Empty(await store.ListAsync(other));
    }

    [Fact]
    public async Task Deleting_a_prefix_removes_only_that_package()
    {
        var store = Store(out var scope);
        using var disposable = scope;
        var org = Guid.NewGuid();
        var doomed = IPackageStore.PrefixOf(org, Guid.NewGuid());
        var kept = IPackageStore.PrefixOf(org, Guid.NewGuid());
        await PutAsync(store, doomed, "a.txt", "a");
        await PutAsync(store, doomed, "deep/b.txt", "b");
        await PutAsync(store, kept, "a.txt", "a");

        var removed = await store.DeletePrefixAsync(doomed);

        Assert.Equal(2, removed);
        Assert.Empty(await store.ListAsync(doomed));
        Assert.Equal(["a.txt"], await store.ListAsync(kept));
        Assert.Equal(0, await store.DeletePrefixAsync(doomed));
    }
}
