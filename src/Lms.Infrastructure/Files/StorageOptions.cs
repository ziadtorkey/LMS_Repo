namespace Lms.Infrastructure.Files;

/// <summary>Bound from the Storage configuration section.</summary>
public sealed class StorageOptions
{
    public const string DefaultAllowedExtensions = ".pdf,.docx,.pptx,.xlsx,.jpg,.jpeg,.png,.mp4,.mp3,.zip,.txt,.gif,.webp,.m4a,.ogg,.wav,.webm";

    /// <summary>S3-compatible endpoint the API talks to, for example http://localhost:9000.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Endpoint put into presigned urls when browsers reach storage under another address; defaults to Endpoint.</summary>
    public string? PublicEndpoint { get; set; }

    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Objects up to this size are read into memory by <c>OpenReadAsync</c>; larger ones (package archives, up to
    /// <c>Files.MaxUploadMb</c>) are streamed from storage into a temporary file that is deleted when the returned stream is disposed,
    /// so a validation job never holds a whole archive on the heap. Default 4 MiB.
    /// </summary>
    public long InMemoryReadLimitBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>Directory for those temporary files; null uses the system temp directory. Size and place it as described in docs/runbook-storage.md.</summary>
    public string? SpoolDirectory { get; set; }

    /// <summary>Comma separated, with the leading dot.</summary>
    public string AllowedExtensions { get; set; } = DefaultAllowedExtensions;

    public HashSet<string> AllowedExtensionSet() =>
        AllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
