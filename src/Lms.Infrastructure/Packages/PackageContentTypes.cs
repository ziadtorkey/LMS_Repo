namespace Lms.Infrastructure.Packages;

/// <summary>A content type the package host sends, and whether the browser may show it (otherwise it is a download).</summary>
public sealed record PackageContentType(string Type, bool Inline);

/// <summary>
/// The extension allow-list of extracted package files (M4b Task 8a). The type of a served file comes from here, never from the archive
/// or from sniffing the bytes; an extension that is not listed is stored as <c>application/octet-stream</c> and served as a download.
/// HTML and SVG are active content: the package host serves them under its sandbox policy.
/// </summary>
public static class PackageContentTypes
{
    public const string Fallback = "application/octet-stream";

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["html"] = "text/html; charset=utf-8", ["htm"] = "text/html; charset=utf-8", ["xhtml"] = "text/html; charset=utf-8",
        ["css"] = "text/css; charset=utf-8", ["js"] = "text/javascript; charset=utf-8", ["mjs"] = "text/javascript; charset=utf-8",
        ["json"] = "application/json", ["xml"] = "application/xml", ["xsd"] = "application/xml", ["txt"] = "text/plain; charset=utf-8",
        ["csv"] = "text/csv; charset=utf-8", ["vtt"] = "text/vtt; charset=utf-8", ["srt"] = "text/plain; charset=utf-8",
        ["png"] = "image/png", ["jpg"] = "image/jpeg", ["jpeg"] = "image/jpeg", ["gif"] = "image/gif", ["webp"] = "image/webp",
        ["svg"] = "image/svg+xml", ["ico"] = "image/x-icon", ["bmp"] = "image/bmp",
        ["mp3"] = "audio/mpeg", ["m4a"] = "audio/mp4", ["ogg"] = "audio/ogg", ["oga"] = "audio/ogg", ["wav"] = "audio/wav",
        ["mp4"] = "video/mp4", ["webm"] = "video/webm", ["ogv"] = "video/ogg",
        ["woff"] = "font/woff", ["woff2"] = "font/woff2", ["ttf"] = "font/ttf", ["otf"] = "font/otf",
        ["pdf"] = "application/pdf", ["wasm"] = "application/wasm",
    };

    /// <summary>The type for a relative path, by its last extension.</summary>
    public static PackageContentType For(string relativePath)
    {
        var extension = Path.GetExtension(relativePath).TrimStart('.');
        return Types.TryGetValue(extension, out var type) ? new PackageContentType(type, true) : new PackageContentType(Fallback, false);
    }
}
