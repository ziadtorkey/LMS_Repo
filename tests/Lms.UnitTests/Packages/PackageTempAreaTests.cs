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
    public async Task Second_validation_for_the_same_client_waits()
    {
        var gate = new PackageValidationGate(Options.Create(new PackageValidationOptions { MaxConcurrent = 2 }));
        var client = Guid.NewGuid();
        var first = await gate.EnterAsync(client, default);

        var second = gate.EnterAsync(client, default);
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        var otherClient = await gate.EnterAsync(Guid.NewGuid(), default);
        await first.DisposeAsync();
        await using var slot = await second.WaitAsync(TimeSpan.FromSeconds(5));
        await otherClient.DisposeAsync();
    }

    [Fact]
    public async Task The_server_runs_at_most_the_configured_number_of_validations()
    {
        var gate = new PackageValidationGate(Options.Create(new PackageValidationOptions { MaxConcurrent = 2 }));
        var a = await gate.EnterAsync(Guid.NewGuid(), default);
        var b = await gate.EnterAsync(Guid.NewGuid(), default);

        var third = gate.EnterAsync(Guid.NewGuid(), default);
        await Task.Delay(100);
        Assert.False(third.IsCompleted);

        await a.DisposeAsync();
        await using var slot = await third.WaitAsync(TimeSpan.FromSeconds(5));
        await b.DisposeAsync();
    }

    [Fact]
    public async Task A_cancelled_wait_gives_the_client_slot_back()
    {
        var gate = new PackageValidationGate(Options.Create(new PackageValidationOptions { MaxConcurrent = 1 }));
        var holder = await gate.EnterAsync(Guid.NewGuid(), default);
        var client = Guid.NewGuid();
        using var cancel = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EnterAsync(client, cancel.Token));
        await holder.DisposeAsync();

        await using var slot = await gate.EnterAsync(client, default).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
