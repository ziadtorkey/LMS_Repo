using System.Text;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Packages.H5p;
using Lms.Infrastructure.Packages.Scorm;
using Lms.IntegrationTests.Packages.Corpus;

namespace Lms.IntegrationTests.Packages.Demo;

/// <summary>The demo packages of the E2E suite: committed bytes equal the builder, the packages pass the archive reader and the real validators, and the page objects' test ids exist.</summary>
public sealed class DemoPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lms-demo-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    public static TheoryData<string> Names => [.. DemoPackages.All.Keys];

    private static string Committed(string name) => Path.Combine(AppContext.BaseDirectory, "Packages", "Demo", "files", name);

    private async Task<(ZipInspection Inspection, PackageTempArea Area)> ExtractAsync(string name)
    {
        await using var archive = new FileStream(Committed(name), FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = await ZipHardening.InspectAsync(archive, new ZipHardeningLimits());
        var area = new PackageTempArea(_root, 16L * 1024 * 1024);
        if (inspection.Accepted)
        {
            await ZipHardening.ExtractAsync(archive, inspection, (entry, content) => area.WriteAsync(entry.Name, content, default));
        }

        return (inspection, area);
    }

    private static string Text(PackageTempArea area, string path)
    {
        using var reader = new StreamReader(area.OpenRead(path), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Committed_files_equal_the_builder_byte_for_byte(string name) =>
        Assert.True(DemoPackages.All[name]().SequenceEqual(File.ReadAllBytes(Committed(name))), $"{name} differs from its builder; regenerate with LMS_REGEN_DEMO=1.");

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Packages_pass_the_archive_reader_and_use_no_external_address(string name)
    {
        var (inspection, area) = await ExtractAsync(name);
        using var disposable = area;

        Assert.True(inspection.Accepted, inspection.RejectReason);
        foreach (var path in area.Files.Where(p => !p.EndsWith("imsmanifest.xml", StringComparison.Ordinal)))
        {
            var text = Text(area, path);
            Assert.DoesNotContain("http://", text);
            Assert.DoesNotContain("https://", text);
            Assert.DoesNotContain("eval(", text);
        }
    }

    [Theory]
    [InlineData(DemoPackages.Scorm12File, "Scorm12", "LMS Demo SCORM 1.2")]
    [InlineData(DemoPackages.Scorm2004File, "Scorm2004", "LMS Demo SCORM 2004")]
    public async Task Scorm_demos_validate_as_single_sco_packages_without_frames(string name, string version, string title)
    {
        var (_, area) = await ExtractAsync(name);
        using var disposable = area;

        var result = await new ScormManifestValidator().ValidateAsync(area, default);

        Assert.True(result.Valid, result.RejectReason);
        Assert.Equal((version, title, "index.html"), (result.Version, result.Title, result.LaunchPath));
    }

    [Fact]
    public async Task The_h5p_demo_validates_with_its_own_library()
    {
        var (_, area) = await ExtractAsync(DemoPackages.H5pFile);
        using var disposable = area;

        var result = await new H5pValidator().ValidateAsync(area, default);

        Assert.True(result.Valid, result.RejectReason);
        Assert.Equal(("LMS Demo Quiz", "LmsDemoQuiz"), (result.Title, result.MainLibrary));
        Assert.Contains("LmsDemoQuiz-1.0/quiz.js", area.Files);
    }

    [Theory]
    [InlineData(DemoPackages.Scorm12File)]
    [InlineData(DemoPackages.Scorm2004File)]
    public async Task Scorm_pages_carry_the_test_ids_of_the_page_objects(string name)
    {
        var (_, area) = await ExtractAsync(name);
        using var disposable = area;

        var page1 = Text(area, "index.html");
        var page2 = Text(area, "page2.html");

        foreach (var id in new[] { "sco-title", "sco-page-1", "sco-api-status", "sco-next" })
        {
            Assert.Contains($"data-testid=\"{id}\"", page1);
        }

        foreach (var id in new[] { "sco-title", "sco-page-2", "sco-api-status", "sco-score", "sco-finish", "sco-result" })
        {
            Assert.Contains($"data-testid=\"{id}\"", page2);
        }

        var js = Text(area, "sco.js");
        Assert.Contains(name == DemoPackages.Scorm12File ? "LMSFinish" : "Terminate", js);
        Assert.Contains("API found", js);
        Assert.Contains("API NOT found", js);
    }

    [Fact]
    public async Task The_h5p_library_carries_the_test_ids_of_the_quiz()
    {
        var (_, area) = await ExtractAsync(DemoPackages.H5pFile);
        using var disposable = area;

        var js = Text(area, "LmsDemoQuiz-1.0/quiz.js");

        foreach (var id in new[] { "h5p-demo-question", "h5p-demo-answer-", "h5p-demo-submit", "h5p-demo-result" })
        {
            Assert.Contains($"'{id}", js.Replace("\"", "'"));
        }

        Assert.Contains("triggerXAPIScored", js);
    }

    [Fact]
    public void The_scorm_demos_complete_with_the_real_tracker_for_the_documented_values()
    {
        var tracker = new ScormTracker();
        var upload12 = (PackageUpload)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PackageUpload));
        typeof(PackageUpload).GetProperty(nameof(PackageUpload.Version))!.SetValue(upload12, "Scorm12");
        var upload2004 = (PackageUpload)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PackageUpload));
        typeof(PackageUpload).GetProperty(nameof(PackageUpload.Version))!.SetValue(upload2004, "Scorm2004");

        // What sco.js reports for the default score of 85 (see files/README.md).
        var v12 = tracker.Judge(upload12, "Score", 60, new Dictionary<string, string>
        {
            ["cmi.core.lesson_status"] = "passed", ["cmi.core.score.raw"] = "85", ["cmi.core.score.min"] = "0", ["cmi.core.score.max"] = "100",
        });
        var v2004 = tracker.Judge(upload2004, "Score", 60, new Dictionary<string, string>
        {
            ["cmi.completion_status"] = "completed", ["cmi.success_status"] = "passed", ["cmi.score.raw"] = "85", ["cmi.score.min"] = "0",
            ["cmi.score.max"] = "100", ["cmi.score.scaled"] = "0.85",
        });

        Assert.True(v12.Completed);
        Assert.True(v2004.Completed);
        Assert.Equal((85m, 100m), (v12.ScoreRaw, v12.ScoreMax));
        Assert.Equal((85m, 100m), (v2004.ScoreRaw, v2004.ScoreMax));
    }

    /// <summary>Regenerates the committed files: <c>LMS_REGEN_DEMO=1 dotnet test --filter Demo_files_are_regenerated</c>. A no-op otherwise.</summary>
    [Fact]
    public void Demo_files_are_regenerated_on_request()
    {
        if (Environment.GetEnvironmentVariable("LMS_REGEN_DEMO") != "1")
        {
            return;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "Lms.IntegrationTests", "Packages", "Demo")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir.FullName, "tests", "Lms.IntegrationTests", "Packages", "Demo", "files");
        Directory.CreateDirectory(target);
        foreach (var (name, build) in DemoPackages.All)
        {
            File.WriteAllBytes(Path.Combine(target, name), build());
        }
    }
}
