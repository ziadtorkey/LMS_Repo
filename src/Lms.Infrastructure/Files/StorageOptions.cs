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

    /// <summary>Comma separated, with the leading dot.</summary>
    public string AllowedExtensions { get; set; } = DefaultAllowedExtensions;

    public HashSet<string> AllowedExtensionSet() =>
        AllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
