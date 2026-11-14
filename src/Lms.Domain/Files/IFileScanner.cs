namespace Lms.Domain.Files;

/// <summary>The verdict of a scan.</summary>
public sealed record FileScanResult(bool IsClean, string? Detail = null);

/// <summary>
/// The antivirus hook (M5 D65): a scan step before a private upload is stored. v1 ships <see cref="NoOpFileScanner"/> and the step is off by default
/// (<c>Excuses:ScanAttachments</c>); a real scanner replaces the registration before production.
/// </summary>
public interface IFileScanner
{
    Task<FileScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default);
}

/// <summary>Accepts everything. The default registration while no scanner is configured.</summary>
public sealed class NoOpFileScanner : IFileScanner
{
    public Task<FileScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default) =>
        Task.FromResult(new FileScanResult(true));
}
