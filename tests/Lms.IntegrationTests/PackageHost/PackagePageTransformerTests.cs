using Lms.Shared.Packages;
using System.Text;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>The HTML seam of the content host: only HTML of the transformer's kind goes through it, with the token's ids (M4b Task 9a).</summary>
[Collection(ApiCollection.Name)]
public sealed class PackagePageTransformerTests(LmsApiFactory factory)
{
    private sealed class Marking(string kind) : IPackagePageTransformer
    {
        public string Kind => kind;
        public PackagePageContext? Seen { get; private set; }

        public Task<byte[]> TransformAsync(PackagePageContext context, string relativePath, byte[] html, CancellationToken ct)
        {
            Seen = context;
            return Task.FromResult(Encoding.UTF8.GetBytes("<!--t-->" + Encoding.UTF8.GetString(html)));
        }
    }

    [Fact]
    public async Task Html_of_the_transformers_kind_is_transformed_with_the_token_ids_and_other_files_are_not()
    {
        var transformer = new Marking(PackageKinds.Scorm);
        using var kit = new PackageHostKit(factory, transformer: transformer);
        var tree = await AccessTestData.SeedAsync(factory, "PT1");
        var package = await kit.SeedPackageAsync(tree.Root, tree.School1, ("index.html", "<p>x</p>"), ("app.js", "var a;"));
        var student = Guid.NewGuid();
        var token = kit.Token(package, student);

        var html = await kit.Content().GetAsync($"/p/{token}/index.html");
        var js = await kit.Content().GetAsync($"/p/{token}/app.js");

        Assert.Equal("<!--t--><p>x</p>", await html.Content.ReadAsStringAsync());
        Assert.Equal(16, html.Content.Headers.ContentLength);
        Assert.True(html.Headers.CacheControl is { NoStore: true });
        Assert.Equal("var a;", await js.Content.ReadAsStringAsync());
        Assert.Equal(new PackagePageContext(package.OrganizationId, package.Id, student, null, false, $"/p/{token}/"), transformer.Seen);
    }

    [Fact]
    public async Task A_transformer_of_another_kind_leaves_the_html_alone()
    {
        using var kit = new PackageHostKit(factory, transformer: new Marking(PackageKinds.H5p));
        var tree = await AccessTestData.SeedAsync(factory, "PT2");
        var package = await kit.SeedPackageAsync(tree.Root, tree.School1, ("index.html", "<p>x</p>"));

        var html = await kit.Content().GetAsync($"/p/{kit.Token(package)}/index.html");

        Assert.Equal("<p>x</p>", await html.Content.ReadAsStringAsync());
    }

    private sealed class Prelude : IPackageHostAsset
    {
        public string Name => "scorm-prelude.abc123.js";
        public byte[] Content => Encoding.UTF8.GetBytes("window.API={};");
        public string ContentType => "text/javascript; charset=utf-8";
        public string Hash => "abc123";
    }

    [Fact]
    public async Task Reserved_lms_path_is_answered_from_the_asset_registry_only_never_from_the_store()
    {
        using var kit = new PackageHostKit(factory, asset: new Prelude());
        var tree = await AccessTestData.SeedAsync(factory, "PT3");
        // A package that ships its own _lms/ files, including one named like the application's asset.
        var package = await kit.SeedPackageAsync(tree.Root, tree.School1,
            ("_lms/scorm-prelude.abc123.js", "evil()"), ("_lms/other.js", "evil()"), ("index.html", "<p/>"));
        var token = kit.Token(package);

        var asset = await kit.Content().GetAsync($"/p/{token}/_lms/scorm-prelude.abc123.js");
        var shadowed = await kit.Content().GetAsync($"/p/{token}/_lms/other.js");
        var unknown = await kit.Content().GetAsync($"/p/{token}/_lms/nothing.js");

        Assert.Equal("window.API={};", await asset.Content.ReadAsStringAsync());
        Assert.StartsWith("sandbox allow-scripts;", asset.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, shadowed.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain(kit.Opened, o => o.Path.StartsWith("_lms/", StringComparison.Ordinal));
    }
}
