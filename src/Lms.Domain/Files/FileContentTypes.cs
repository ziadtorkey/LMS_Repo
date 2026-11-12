namespace Lms.Domain.Files;

/// <summary>
/// The content type of a stored file is decided by its (allow-listed) extension, never by the client: a browser would
/// otherwise render an uploaded "x.png" that is really HTML as a page.
/// </summary>
public static class FileContentTypes
{
    public const string Fallback = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".mp4"] = "video/mp4",
        [".mp3"] = "audio/mpeg",
        [".zip"] = "application/zip",
    };

    public static string ForExtension(string extension) =>
        ByExtension.TryGetValue(extension, out var type) ? type : Fallback;

    /// <summary>Content-Disposition value that forces a download under the original file name (RFC 6266 / 5987).</summary>
    public static string AttachmentDisposition(string fileName, string extension) =>
        $"attachment; filename=\"download{extension.ToLowerInvariant()}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
}
