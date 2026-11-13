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
        Assert.Equal(new PackagePageContext(package.OrganizationId, package.Id, student, null, false), transformer.Seen);
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
}
