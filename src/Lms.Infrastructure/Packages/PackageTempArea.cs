using Lms.Domain.Packages;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// The scratch directory of one validation job (M4b Task 8b): every extracted file goes under its own root, a write is refused when its path
/// would leave the root or the job's disk budget would be exceeded, and disposing removes the whole directory (also when the job fails).
/// </summary>
public sealed class PackageTempArea : IPackageContent, IDisposable
{
    private readonly List<string> _files = [];
    private readonly string _rootWithSeparator;
    private long _written;

    public PackageTempArea(string parent, long budgetBytes)
    {
        Root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        _rootWithSeparator = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        BudgetBytes = budgetBytes;
    }

    public string Root { get; }

    public long BudgetBytes { get; }

    public long WrittenBytes => _written;

    public IReadOnlyList<string> Files => _files;

    /// <summary>A scratch file next to the extracted files (not part of the package), for example the archive copy; counts toward no budget.</summary>
    public string NewScratchPath() => Path.Combine(Root, ".scratch-" + Guid.NewGuid().ToString("N"));

    public bool Exists(string relativePath) => _files.Contains(relativePath, StringComparer.Ordinal);

    public Stream OpenRead(string relativePath) =>
        Exists(relativePath)
            ? new FileStream(Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : throw new FileNotFoundException("Not a file of the package.", relativePath);

    /// <summary>Writes one extracted file; throws <see cref="ZipHardeningException"/> when the path leaves the area or the budget is used up.</summary>
    public async Task WriteAsync(string relativePath, Stream content, CancellationToken ct)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) > 0)
        {
            if (_written + read > BudgetBytes)
            {
                throw new ZipHardeningException(Lms.Shared.Packages.PackageRejectReasons.too_large, "The job disk budget is used up.");
            }

            _written += read;
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        _files.Add(relativePath);
    }

    /// <summary>The full path of a relative one; a path that escapes the root is refused (the hardening reader never lets one through, this is the second wall).</summary>
    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!full.StartsWith(_rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ZipHardeningException(Lms.Shared.Packages.PackageRejectReasons.unsafe_path, "A path leaves the temp area.");
        }

        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
            // Still in use (antivirus, a slow reader): the sweep job removes it later.
        }
    }
}
