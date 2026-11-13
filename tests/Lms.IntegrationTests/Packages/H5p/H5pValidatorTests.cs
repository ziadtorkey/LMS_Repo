using System.Text;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Packages.H5p;
using Lms.IntegrationTests.Packages.Corpus;

namespace Lms.IntegrationTests.Packages.H5p;

/// <summary>The H5P validator (M4b Task 11, host-independent half) on the corpus and on hand-made packages: archives are hardened and extracted like the validation job does.</summary>
public sealed class H5pValidatorTests : IDisposable
{
    private const string Main = "{\"title\":\"Corpus\",\"mainLibrary\":\"H5P.Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P.Foo\",\"majorVersion\":1,\"minorVersion\":0}]}";

    private const string Library = "{\"machineName\":\"H5P.Foo\",\"majorVersion\":1,\"minorVersion\":0,\"title\":\"Foo\"}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lms-h5p-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<PackageValidationResult> ValidateBytesAsync(byte[] zip)
    {
        await using var archive = new MemoryStream(zip);
        var inspection = await ZipHardening.InspectAsync(archive, new ZipHardeningLimits());
        Assert.True(inspection.Accepted, inspection.RejectReason);
        using var area = new PackageTempArea(_root, 64L * 1024 * 1024);
        await ZipHardening.ExtractAsync(archive, inspection, (entry, content) => area.WriteAsync(entry.Name, content, default));
        return await new H5pValidator().ValidateAsync(area, default);
    }

    private Task<PackageValidationResult> ValidateCorpusAsync(string name) =>
        ValidateBytesAsync(File.ReadAllBytes(CorpusCases.CommittedFile(CorpusCases.All.Single(c => c.Name == name))));

    private static byte[] Zip(params (string Name, string Text)[] files) =>
        RawZipWriter.Write(files.Select(f => RawZipWriter.File(f.Name, Encoding.UTF8.GetBytes(f.Text))));

    private static (string, string) ManifestFile(string json) => ("h5p.json", json);

    private static readonly (string, string) ContentFile = ("content/content.json", "{}");

    private static readonly (string, string) LibraryFile = ("H5P.Foo-1.0/library.json", Library);

    [Fact]
    public async Task A_valid_package_is_read_for_its_title_and_main_library()
    {
        var result = await ValidateCorpusAsync("h5p_valid_minimal");

        Assert.True(result.Valid);
        Assert.Equal(("Corpus", "H5P.Foo", null), (result.Title, result.MainLibrary, result.Version));
    }

    [Theory]
    [InlineData("h5p_missing_json")]
    [InlineData("h5p_json_not_json")]
    [InlineData("h5p_json_deeply_nested")]
    [InlineData("h5p_json_huge")]
    [InlineData("h5p_main_library_traversal")]
    public async Task Invalid_deep_huge_or_traversing_h5p_json_is_rejected(string corpus)
    {
        var result = await ValidateCorpusAsync(corpus);

        Assert.Equal("h5p_invalid", result.RejectReason);
        Assert.Equal(CorpusCases.All.Single(c => c.Name == corpus).Expect, result.RejectReason);
    }

    public static TheoryData<string, string> BrokenPackages => new()
    {
        { "no title", "{\"mainLibrary\":\"H5P.Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P.Foo\",\"majorVersion\":1,\"minorVersion\":0}]}" },
        { "main library not a dependency", "{\"title\":\"t\",\"mainLibrary\":\"H5P.Bar\",\"preloadedDependencies\":[{\"machineName\":\"H5P.Foo\",\"majorVersion\":1,\"minorVersion\":0}]}" },
        { "dependencies not an array", "{\"title\":\"t\",\"mainLibrary\":\"H5P.Foo\",\"preloadedDependencies\":{}}" },
        { "no dependencies", "{\"title\":\"t\",\"mainLibrary\":\"H5P.Foo\"}" },
        { "empty dependencies", "{\"title\":\"t\",\"mainLibrary\":\"H5P.Foo\",\"preloadedDependencies\":[]}" },
        { "machine name with a slash", "{\"title\":\"t\",\"mainLibrary\":\"H5P/Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P/Foo\",\"majorVersion\":1,\"minorVersion\":0}]}" },
        { "machine name with a backslash", "{\"title\":\"t\",\"mainLibrary\":\"H5P\\\\Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P\\\\Foo\",\"majorVersion\":1,\"minorVersion\":0}]}" },
        { "version not a number", "{\"title\":\"t\",\"mainLibrary\":\"H5P.Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P.Foo\",\"majorVersion\":\"1\",\"minorVersion\":0}]}" },
        { "negative version", "{\"title\":\"t\",\"mainLibrary\":\"H5P.Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P.Foo\",\"majorVersion\":-1,\"minorVersion\":0}]}" },
        { "root is an array", "[]" },
    };

    [Theory]
    [MemberData(nameof(BrokenPackages))]
    public async Task A_broken_h5p_json_is_rejected(string why, string manifest)
    {
        var result = await ValidateBytesAsync(Zip(ManifestFile(manifest), ContentFile, LibraryFile));

        Assert.Equal("h5p_invalid", result.RejectReason);
        Assert.NotNull(why);
    }

    [Fact]
    public async Task Missing_content_or_library_files_are_rejected()
    {
        var noContent = await ValidateBytesAsync(Zip(ManifestFile(Main), LibraryFile));
        var noLibrary = await ValidateBytesAsync(Zip(ManifestFile(Main), ContentFile));
        var contentNotJson = await ValidateBytesAsync(Zip(ManifestFile(Main), ("content/content.json", "<html>"), LibraryFile));
        var contentScalar = await ValidateBytesAsync(Zip(ManifestFile(Main), ("content/content.json", "42"), LibraryFile));

        Assert.All(new[] { noContent, noLibrary, contentNotJson, contentScalar }, r => Assert.Equal("h5p_invalid", r.RejectReason));
    }

    [Fact]
    public async Task A_library_json_must_say_the_same_name_and_version_as_its_folder()
    {
        var wrongName = await ValidateBytesAsync(Zip(ManifestFile(Main), ContentFile, ("H5P.Foo-1.0/library.json", Library.Replace("H5P.Foo", "H5P.Evil"))));
        var wrongVersion = await ValidateBytesAsync(Zip(ManifestFile(Main), ContentFile, ("H5P.Foo-1.0/library.json", Library.Replace("\"minorVersion\":0", "\"minorVersion\":9"))));
        var notJson = await ValidateBytesAsync(Zip(ManifestFile(Main), ContentFile, ("H5P.Foo-1.0/library.json", "nope")));

        Assert.All(new[] { wrongName, wrongVersion, notJson }, r => Assert.Equal("h5p_invalid", r.RejectReason));
    }

    [Fact]
    public async Task Libraries_needed_by_other_libraries_must_be_present_too()
    {
        var needsBar = Library.Replace("\"title\":\"Foo\"", "\"title\":\"Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P.Bar\",\"majorVersion\":2,\"minorVersion\":1}]");
        var bar = "{\"machineName\":\"H5P.Bar\",\"majorVersion\":2,\"minorVersion\":1}";

        var missing = await ValidateBytesAsync(Zip(ManifestFile(Main), ContentFile, ("H5P.Foo-1.0/library.json", needsBar)));
        var present = await ValidateBytesAsync(Zip(ManifestFile(Main), ContentFile, ("H5P.Foo-1.0/library.json", needsBar), ("H5P.Bar-2.1/library.json", bar)));

        Assert.Equal("h5p_invalid", missing.RejectReason);
        Assert.True(present.Valid);
    }

    [Fact]
    public async Task A_cycle_of_libraries_terminates()
    {
        var foo = Library.Replace("\"title\":\"Foo\"", "\"title\":\"Foo\",\"preloadedDependencies\":[{\"machineName\":\"H5P.Bar\",\"majorVersion\":1,\"minorVersion\":0}]");
        var bar = "{\"machineName\":\"H5P.Bar\",\"majorVersion\":1,\"minorVersion\":0,\"preloadedDependencies\":[{\"machineName\":\"H5P.Foo\",\"majorVersion\":1,\"minorVersion\":0}]}";

        var result = await ValidateBytesAsync(Zip(ManifestFile(Main), ContentFile, ("H5P.Foo-1.0/library.json", foo), ("H5P.Bar-1.0/library.json", bar)));

        Assert.True(result.Valid);
    }

    [Fact]
    public void The_validator_is_for_the_h5p_kind() => Assert.Equal("H5p", new H5pValidator().Kind);
}
