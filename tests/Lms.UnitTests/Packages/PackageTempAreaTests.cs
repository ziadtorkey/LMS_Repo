using Lms.Infrastructure.Packages;
using Microsoft.Extensions.Options;

namespace Lms.UnitTests.Packages;

/// <summary>The scratch directory and the concurrency gate of the validation job (M4b Task 8b).</summary>
public sealed class PackageTempAreaTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "lms-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_parent))
        {
            Directory.Delete(_parent, recursive: true);
        }
    }

    private static MemoryStream Bytes(int count) => new(new byte[count]);

    [Fact]
    public async Task Files_are_written_under_the_area_and_removed_with_it()
    {
        string root;
        using (var area = new PackageTempArea(_parent, 1000))
        {
            root = area.Root;
            await area.WriteAsync("a/b/c.txt", Bytes(10), default);

            Assert.True(File.Exists(Path.Combine(root, "a", "b", "c.txt")));
            Assert.Equal(["a/b/c.txt"], area.Files);
            Assert.Equal(10, area.WrittenBytes);
            await using var read = area.OpenRead("a/b/c.txt");
            Assert.Equal(10, read.Length);
        }

        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("/absolute.txt")]
    public async Task No_file_is_written_outside_the_job_temp_area(string path)
    {
        using var area = new PackageTempArea(_parent, 1000);

        var ex = await Assert.ThrowsAsync<ZipHardeningException>(() => area.WriteAsync(path, Bytes(1), default));

        Assert.Equal("unsafe_path", ex.Reason);
        Assert.Empty(Directory.EnumerateFiles(_parent, "*", SearchOption.TopDirectoryOnly));
        Assert.Empty(area.Files);
    }

    [Fact]
    public async Task Job_stays_inside_the_disk_budget()
    {
        using var area = new PackageTempArea(_parent, 100);
        await area.WriteAsync("one.bin", Bytes(60), default);

        var ex = await Assert.ThrowsAsync<ZipHardeningException>(() => area.WriteAsync("two.bin", Bytes(60), default));

        Assert.Equal("too_large", ex.Reason);
        Assert.True(area.WrittenBytes <= 100);
    }

    [Fact]
    public void A_file_that_was_not_extracted_cannot_be_opened()
    {
        using var area = new PackageTempArea(_parent, 100);

        Assert.Throws<FileNotFoundException>(() => area.OpenRead("missing.txt"));
    }

    [Fact]
    public async Task A_second_validation_for_the_same_client_does_not_wait_it_is_told_to_come_back()
    {
        var gate = new PackageValidationGate(Options.Create(new PackageValidationOptions { MaxConcurrent = 2 }));
        var client = Guid.NewGuid();
        var first = gate.TryEnter(client);
        Assert.NotNull(first);

        // Returns at once (a worker is never held), and another client still gets in.
        Assert.Null(gate.TryEnter(client));
        var other = gate.TryEnter(Guid.NewGuid());
        Assert.NotNull(other);

        await first.DisposeAsync();
        var again = gate.TryEnter(client);
        Assert.NotNull(again);
        await again.DisposeAsync();
        await other.DisposeAsync();
    }

    [Fact]
    public async Task The_server_runs_at_most_the_configured_number_of_validations()
    {
        var gate = new PackageValidationGate(Options.Create(new PackageValidationOptions { MaxConcurrent = 2 }));
        var a = gate.TryEnter(Guid.NewGuid());
        var b = gate.TryEnter(Guid.NewGuid());
        var thirdClient = Guid.NewGuid();

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Null(gate.TryEnter(thirdClient));

        await a.DisposeAsync();
        var third = gate.TryEnter(thirdClient);
        Assert.NotNull(third);
        await third.DisposeAsync();
        await b.DisposeAsync();
    }

    [Fact]
    public async Task A_refused_entry_leaves_no_slot_taken()
    {
        var gate = new PackageValidationGate(Options.Create(new PackageValidationOptions { MaxConcurrent = 1 }));
        var holder = gate.TryEnter(Guid.NewGuid());
        var client = Guid.NewGuid();

        Assert.Null(gate.TryEnter(client)); // the server is full; the client slot it briefly took is given back
        await holder!.DisposeAsync();

        var slot = gate.TryEnter(client);
        Assert.NotNull(slot);
        await slot.DisposeAsync();
    }

    [Fact]
    public async Task The_temp_sweep_removes_only_old_job_areas()
    {
        var oldArea = Directory.CreateDirectory(Path.Combine(_parent, Guid.NewGuid().ToString("N")));
        var freshArea = Directory.CreateDirectory(Path.Combine(_parent, Guid.NewGuid().ToString("N")));
        var foreign = Directory.CreateDirectory(Path.Combine(_parent, "important-data"));
        var hyphenated = Directory.CreateDirectory(Path.Combine(_parent, Guid.NewGuid().ToString("D")));
        var past = DateTime.UtcNow - PackageTempSweepJob.MaxAge - TimeSpan.FromHours(1);
        foreach (var d in new[] { oldArea, foreign, hyphenated })
        {
            Directory.SetLastWriteTimeUtc(d.FullName, past);
        }

        await new PackageTempSweepJob(Options.Create(new PackageValidationOptions { TempRoot = _parent }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PackageTempSweepJob>.Instance).RunAsync();

        Assert.False(Directory.Exists(oldArea.FullName));
        Assert.True(Directory.Exists(freshArea.FullName));
        Assert.True(Directory.Exists(foreign.FullName));
        Assert.True(Directory.Exists(hyphenated.FullName));
    }
}
